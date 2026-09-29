/* ImageSpace resident WebGPU filter engine. MIT. No Uno/.NET dependency. */
(() => {
  'use strict';
  const MiB = 1024 * 1024;
  const budget = 256 * MiB;
  const kinds = new Map(['Invert', 'Grayscale', 'Sepia', 'BrightnessContrast', 'Saturation',
    'Gamma', 'Threshold', 'Posterize', 'GaussianBlur', 'Sharpen', 'Emboss', 'Edges', 'Pixelate'].map((name, i) => [name, i]));
  let current = null, initializing = null, serial = Promise.resolve(), reserved = 0;
  let description = 'Not initialized', generation = 0, nextHandle = 1;
  const sessions = new Set(), handles = new Map();
  const exclusive = action => {
    const result = serial.then(action);
    serial = result.catch(() => {});
    return result;
  };
  const header = `
struct Parameters {
  width:u32, height:u32, kind:u32, radius:u32,
  amount:f32, secondary:f32, size:u32, reserved:u32,
  weights:array<vec4f,49>
}
@group(0) @binding(2) var<uniform> p:Parameters;
fn decode(v:u32)->vec4f { return vec4f(f32(v&255u),f32((v>>8u)&255u),f32((v>>16u)&255u),f32(v>>24u)); }
fn evenRound(v:f32)->u32 {
  let c=clamp(v,0.0,255.0); let lo=floor(c); let f=c-lo;
  return u32(lo)+select(0u,1u,(f>0.5)||((f==0.5)&&((u32(lo)&1u)==1u)));
}
fn pack(v:vec4f)->u32 { return evenRound(v.x)|(evenRound(v.y)<<8u)|(evenRound(v.z)<<16u)|(evenRound(v.w)<<24u); }
fn offset(x:i32,y:i32)->u32 { return u32(clamp(y,0,i32(p.height)-1))*p.width+u32(clamp(x,0,i32(p.width)-1)); }
fn weight(i:i32)->f32 { let at=u32(i+i32(p.radius)); return p.weights[at/4u][at%4u]; }
`;
  // Reused by ordinary and fused pipelines. Pack/decode remains between logical
  // operations: shader fusion must NOT replace the application's RGBA8 stage rounding.
  const colorFunctions = `
fn colorPixel(value:u32, kind:u32, amount:f32, secondary:f32)->u32 {
  let v=decode(value);
  if (v.w==0.0) { return 0u; }
  let c=v.xyz; let l=dot(c,vec3f(0.2126,0.7152,0.0722)); var rgb=c;
  switch kind {
    case 0u: { rgb=vec3f(255.0)-c; }
    case 1u: { rgb=vec3f(l); }
    case 2u: { rgb=vec3f(dot(c,vec3f(.393,.769,.189)),dot(c,vec3f(.349,.686,.168)),dot(c,vec3f(.272,.534,.131))); }
    case 3u: { let contrast=1.0+clamp(secondary,-99.0,300.0)/100.0; rgb=(c-vec3f(127.5))*contrast+vec3f(127.5+amount*2.55); }
    case 4u: { rgb=vec3f(l)+(c-vec3f(l))*max(0.0,1.0+amount/100.0); }
    case 5u: { rgb=255.0*pow(c/255.0,vec3f(1.0/clamp(amount,.1,10.0))); }
    case 6u: { rgb=select(vec3f(0.0),vec3f(255.0),l>=clamp(amount,0.0,255.0)); }
    case 7u: {
      let levels=f32(clamp(i32(amount),2,256)-1); let q=c/255.0*levels;
      rgb=vec3f(f32(evenRound(q.x)),f32(evenRound(q.y)),f32(evenRound(q.z)))*255.0/levels;
    }
    default: {}
  }
  return pack(vec4f(rgb,v.w));
}`;
  const pointCode = header + colorFunctions + `
@group(0) @binding(0) var<storage,read> input:array<u32>;
@group(0) @binding(1) var<storage,read_write> output:array<u32>;
@compute @workgroup_size(8,8)
fn main(@builtin(global_invocation_id) id:vec3u) {
  if ((id.x>=p.width)||(id.y>=p.height)) { return; }
  let i=id.y*p.width+id.x;
  if (p.kind<8u) { output[i]=colorPixel(input[i],p.kind,p.amount,p.secondary); return; }
  let v=decode(input[i]);
  // Spatial operations intentionally retain hidden RGB for subsequent stages.
  var sum=vec3f(0.0); let strength=clamp(select(p.amount,1.0,p.amount==0.0),.1,5.0);
  for (var y=-1;y<=1;y++) { for (var x=-1;x<=1;x++) {
    let at=(y+1)*3+x+1; var w=0.0;
    if (p.kind==10u) { let kernel=array<f32,9>(-2,-1,0,-1,1,1,0,1,2); w=kernel[at]; }
    else if (p.kind==11u) { w=select(-1.0,8.0,at==4); }
    else if (at==4) { w=1.0+4.0*strength; }
    else if ((abs(x)+abs(y))==1) { w=-strength; }
    sum+=decode(input[offset(i32(id.x)+x,i32(id.y)+y)]).xyz*w;
  }}
  output[i]=pack(vec4f(sum+vec3f(select(0.0,128.0,p.kind==10u)),v.w));
}`;
  const fusedCode = header + colorFunctions + `
@group(0) @binding(0) var<storage,read> input:array<u32>;
@group(0) @binding(1) var<storage,read_write> output:array<u32>;
@compute @workgroup_size(8,8)
fn main(@builtin(global_invocation_id) id:vec3u) {
  if ((id.x>=p.width)||(id.y>=p.height)) { return; }
  let pixel=id.y*p.width+id.x; var value=input[pixel];
  for (var i=0u;i<p.radius;i++) {
    let operation=p.weights[i];
    value=colorPixel(value,u32(operation.x),operation.y,operation.z);
  }
  output[pixel]=value;
}`;
  const horizontalCode = header + `
@group(0) @binding(0) var<storage,read> input:array<u32>;
@group(0) @binding(1) var<storage,read_write> output:array<vec4f>;
@compute @workgroup_size(8,8)
fn main(@builtin(global_invocation_id) id:vec3u) {
  if ((id.x>=p.width)||(id.y>=p.height)) { return; }
  var sum=vec4f(0.0);
  for (var x=-i32(p.radius);x<=i32(p.radius);x++) {
    let c=decode(input[offset(i32(id.x)+x,i32(id.y))]); let a=c.w/255.0;
    sum+=vec4f(c.xyz*a,a)*weight(x);
  }
  output[id.y*p.width+id.x]=sum;
}`;
  const verticalCode = header + `
@group(0) @binding(0) var<storage,read> input:array<vec4f>;
@group(0) @binding(1) var<storage,read_write> output:array<u32>;
@compute @workgroup_size(8,8)
fn main(@builtin(global_invocation_id) id:vec3u) {
  if ((id.x>=p.width)||(id.y>=p.height)) { return; }
  var sum=vec4f(0.0);
  for (var y=-i32(p.radius);y<=i32(p.radius);y++) { sum+=input[offset(i32(id.x),i32(id.y)+y)]*weight(y); }
  var value=0u;
  if (sum.w>0.0) { value=pack(vec4f(sum.xyz/sum.w,sum.w*255.0)); }
  output[id.y*p.width+id.x]=value;
}`;
  const pixelateCode = header + `
@group(0) @binding(0) var<storage,read> input:array<u32>;
@group(0) @binding(1) var<storage,read_write> output:array<u32>;
var<workgroup> sums:array<vec4u,64>;
@compute @workgroup_size(64)
fn main(@builtin(workgroup_id) group:vec3u,@builtin(local_invocation_index) lane:u32) {
  let left=group.x*p.size; let top=group.y*p.size;
  let width=min(p.size,p.width-left); let height=min(p.size,p.height-top); let count=width*height;
  var sum=vec4u(0u);
  for (var n=lane;n<count;n+=64u) {
    let v=input[(top+n/width)*p.width+left+n%width]; let a=v>>24u;
    sum+=vec4u((v&255u)*a,((v>>8u)&255u)*a,((v>>16u)&255u)*a,a);
  }
  sums[lane]=sum; workgroupBarrier();
  for (var stride=32u;stride>0u;stride/=2u) {
    if (lane<stride) { sums[lane]+=sums[lane+stride]; }
    workgroupBarrier();
  }
  let total=sums[0]; var value=0u;
  if (total.w>0u) { value=pack(vec4f(vec3f(total.xyz)/f32(total.w),f32(total.w)/f32(count))); }
  for (var n=lane;n<count;n+=64u) { output[(top+n/width)*p.width+left+n%width]=value; }
}`;
  async function scoped(device, action) {
    device.pushErrorScope('validation'); device.pushErrorScope('out-of-memory'); device.pushErrorScope('internal');
    let result, failure;
    try { result = await action(); } catch (error) { failure = error; }
    for (let i = 0; i < 3; i++) {
      try { const error = await device.popErrorScope(); if (error && !failure) failure = new Error(error.message); }
      catch (error) { failure ??= error; }
    }
    if (failure) throw failure;
    return result;
  }
  async function initialize() {
    if (current) return current.device;
    if (initializing) return initializing;
    initializing = (async () => {
      if (!globalThis.navigator?.gpu) { description = 'WebGPU unavailable'; return null; }
      const adapter = await navigator.gpu.requestAdapter({ powerPreference: 'high-performance' });
      if (!adapter) { description = 'No WebGPU adapter'; return null; }
      const device = await adapter.requestDevice();
      let next;
      try {
        next = await scoped(device, async () => {
          const layout = device.createBindGroupLayout({ entries: [
            { binding: 0, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'read-only-storage' } },
            { binding: 1, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
            { binding: 2, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'uniform', hasDynamicOffset: true, minBindingSize: 816 } }
          ] });
          const pipelineLayout = device.createPipelineLayout({ bindGroupLayouts: [layout] });
          const pipelines = {};
          for (const [name, code] of Object.entries({ point: pointCode, fused: fusedCode, horizontal: horizontalCode, vertical: verticalCode, pixelate: pixelateCode })) {
            const module = device.createShaderModule({ label: 'ImageSpace ' + name, code });
            const errors = (await module.getCompilationInfo()).messages.filter(message => message.type === 'error');
            if (errors.length) throw new Error(errors.map(e => `${name} ${e.lineNum}:${e.linePos} ${e.message}`).join('\n'));
            pipelines[name] = await device.createComputePipelineAsync({ label: 'ImageSpace ' + name, layout: pipelineLayout, compute: { module, entryPoint: 'main' } });
          }
          return { device, layout, pipelines, generation: ++generation };
        });
      } catch (error) { device.destroy(); throw error; }
      current = next;
      description = 'WebGPU · ' + (adapter.info?.description || adapter.info?.architecture || adapter.info?.vendor || 'compatible adapter');
      device.lost.then(info => {
        for (const session of [...sessions]) if (session.generation === next.generation) session.dispose();
        for (const [handle, session] of handles) if (session.generation === next.generation) handles.delete(handle);
        if (current === next) { current = null; description = 'Device lost: ' + info.message; }
      });
      return device;
    })().catch(error => { description = 'WebGPU initialization failed: ' + error.message; return null; })
      .finally(() => { initializing = null; });
    return initializing;
  }
  function validatePixels(bytes, width, height) {
    if (!(bytes instanceof Uint8Array) || !Number.isInteger(width) || !Number.isInteger(height) ||
        width < 1 || height < 1 || width > 8192 || height > 8192 || width * height > 16777216 || bytes.byteLength !== width * height * 4)
      throw new RangeError('Expected RGBA8 bytes, 1–8192 pixels per side and at most 16 megapixels.');
  }
  function normalize(operations) {
    if (!Array.isArray(operations) || operations.length > 16) throw new RangeError('A filter stack contains at most sixteen operations.');
    return operations.map(operation => {
      if (!operation || typeof operation.kind !== 'string') throw new TypeError('Every operation requires a filter kind.');
      const amount = operation.amount ?? 0, secondary = operation.secondary ?? 0;
      if (!Number.isFinite(amount) || !Number.isFinite(secondary) || Math.abs(amount) > 1000000 || Math.abs(secondary) > 1000000)
        throw new RangeError('Filter parameters must be finite and within ±1000000.');
      return { kind: operation.kind, amount, secondary, enabled: operation.enabled !== false };
    }).filter(operation => operation.enabled && !(operation.kind === 'GaussianBlur' && operation.amount <= 0));
  }
  async function createSession(bytes, width, height, options = {}) {
    if (!options || typeof options !== 'object' ||
        (options.fuseColorOperations !== undefined && typeof options.fuseColorOperations !== 'boolean'))
      throw new TypeError('fuseColorOperations must be a boolean.');
    const fuseColors = options.fuseColorOperations !== false;
    validatePixels(bytes, width, height);
    const snapshot = bytes.slice();
    if (!await initialize()) return null;
    return exclusive(async () => {
      const runtime = current;
      if (!runtime) return null;
      const { device, layout, pipelines } = runtime, length = snapshot.byteLength;
      const stride = Math.ceil(816 / device.limits.minUniformBufferOffsetAlignment) * device.limits.minUniformBufferOffsetAlignment;
      const fixedBytes = length * 4 + stride * 16;
      if (length > device.limits.maxStorageBufferBindingSize || length > device.limits.maxBufferSize || reserved + fixedBytes > budget) return null;
      const resources = [], bindings = new Map();
      let closed = false, scratch = null, last = null, owned = 0;
      const stats = { sourceUploads: 0, uploadedBytes: 0, submissions: 0, dispatches: 0, readbacks: 0, bufferAllocations: 0, bindGroupBuilds: 0, logicalOperations: 0, fusedPasses: 0, parameterBytesUploaded: 0 };
      const allocate = (size, usage, label) => {
        if (reserved + size > budget || size > device.limits.maxBufferSize) throw new RangeError('Resident GPU memory budget exceeded.');
        const buffer = device.createBuffer({ size, usage, label: 'ImageSpace ' + label });
        resources.push(buffer); reserved += size; owned += size; stats.bufferAllocations++;
        return buffer;
      };
      // A session owns one reusable CPU parameter arena as well as its GPU arena.
      const parameters = new ArrayBuffer(stride * 16);
      const parameterBytes = new Uint8Array(parameters);
      let source, ping, readback, uniforms;
      const ensure = () => {
        if (closed || current !== runtime) throw new Error('The resident filter session is disposed or its GPU device was lost.');
      };
      const dispose = () => {
        if (closed) return;
        closed = true;
        for (const buffer of resources) buffer.destroy();
        reserved -= owned; owned = 0; resources.length = 0; bindings.clear(); sessions.delete(api);
      };
      const readOutput = async () => {
        ensure();
        const encoder = device.createCommandEncoder({ label: 'ImageSpace readback' });
        encoder.copyBufferToBuffer(last ?? source, 0, readback, 0, length);
        device.queue.submit([encoder.finish()]); stats.submissions++;
        let timer;
        try {
          await Promise.race([readback.mapAsync(GPUMapMode.READ), new Promise((_, reject) => {
            timer = setTimeout(() => reject(new Error('GPU readback timed out.')), 30000);
          })]);
          ensure();
          const result = new Uint8Array(readback.getMappedRange()).slice();
          stats.readbacks++;
          return result;
        } finally { clearTimeout(timer); if (!closed) readback.unmap(); }
      };
      const dispatch = operations => {
        ensure();
        if (operations.some(op => !kinds.has(op.kind))) throw new Error('Unsupported filter in the resident stack.');
        if (operations.some(op => op.kind === 'GaussianBlur') && !scratch) {
          if (length * 4 > device.limits.maxStorageBufferBindingSize) throw new RangeError('Blur scratch exceeds the adapter storage-binding limit.');
          scratch = allocate(length * 4, GPUBufferUsage.STORAGE, 'premultiplied blur scratch');
        }
        parameterBytes.fill(0);
        const plan = [];
        let input = source, slot = 0, targetIndex = 0;
        for (let index = 0; index < operations.length;) {
          const op = operations[index];
          let end = index + 1;
          if (fuseColors && kinds.get(op.kind) < 8)
            while (end < operations.length && kinds.get(operations[end].kind) < 8) end++;
          const at = slot * stride;
          const integers = new Uint32Array(parameters, at, 8), floats = new Float32Array(parameters, at, 204);
          integers.set([width, height, kinds.get(op.kind), 0]);
          floats[4] = op.amount; floats[5] = op.secondary;
          integers[6] = Math.max(2, Math.min(128, Math.trunc(op.amount)));
          const output = ping[targetIndex]; targetIndex ^= 1;
          if (end - index > 1) {
            integers[3] = end - index;
            for (let i = index; i < end; i++) {
              const operation = operations[i], offset = 8 + (i - index) * 4;
              floats[offset] = kinds.get(operation.kind);
              floats[offset + 1] = operation.amount; floats[offset + 2] = operation.secondary;
            }
            plan.push({ name: 'fused', input, output, slot });
          } else if (op.kind === 'GaussianBlur') {
            const sigma = Math.max(.1, Math.min(32, op.amount)), radius = Math.ceil(sigma * 3);
            integers[3] = radius;
            let total = 0; const weights = [];
            for (let i = -radius; i <= radius; i++) { const w = Math.exp(-i * i / (2 * sigma * sigma)); weights.push(w); total += w; }
            weights.forEach((w, i) => { floats[8 + i] = w / total; });
            plan.push({ name: 'horizontal', input, output: scratch, slot });
            plan.push({ name: 'vertical', input: scratch, output, slot });
          } else plan.push({ name: op.kind === 'Pixelate' ? 'pixelate' : 'point', input, output, slot, size: integers[6] });
          input = output; slot++; index = end;
        }
        if (!plan.length) { last = source; return; }
        device.queue.writeBuffer(uniforms, 0, parameters, 0, slot * stride);
        stats.parameterBytesUploaded += slot * stride;
        const encoder = device.createCommandEncoder({ label: 'ImageSpace resident filter stack' });
        for (const op of plan) {
          const key = resources.indexOf(op.input) + ':' + resources.indexOf(op.output);
          let binding = bindings.get(key);
          if (!binding) {
            binding = device.createBindGroup({ layout, entries: [
              { binding: 0, resource: { buffer: op.input } }, { binding: 1, resource: { buffer: op.output } },
              { binding: 2, resource: { buffer: uniforms, size: 816 } }
            ] });
            bindings.set(key, binding); stats.bindGroupBuilds++;
          }
          const pass = encoder.beginComputePass();
          pass.setPipeline(pipelines[op.name]); pass.setBindGroup(0, binding, [op.slot * stride]);
          if (op.name === 'pixelate') pass.dispatchWorkgroups(Math.ceil(width / op.size), Math.ceil(height / op.size));
          else pass.dispatchWorkgroups(Math.ceil(width / 8), Math.ceil(height / 8));
          pass.end(); stats.dispatches++;
          if (op.name === 'fused') stats.fusedPasses++;
        }
        device.queue.submit([encoder.finish()]); stats.submissions++; stats.logicalOperations += operations.length; last = input;
      };
      const run = (operations, read) => {
        const captured = normalize(operations);
        return exclusive(async () => {
          ensure();
          try { return await scoped(device, async () => { dispatch(captured); return read ? await readOutput() : undefined; }); }
          catch (error) { dispose(); throw error; }
        });
      };
      const api = Object.freeze({
        width, height, generation: runtime.generation,
        apply: operations => run(operations, true), execute: operations => run(operations, false),
        read: () => exclusive(async () => {
          ensure();
          try { return await scoped(device, readOutput); } catch (error) { dispose(); throw error; }
        }),
        statistics: () => ({ ...stats, residentBytes: owned, disposed: closed }), dispose
      });
      try {
        await scoped(device, () => {
          const usage = GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC | GPUBufferUsage.COPY_DST;
          source = allocate(length, usage, 'immutable source');
          ping = [allocate(length, usage, 'ping'), allocate(length, usage, 'pong')];
          readback = allocate(length, GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST, 'readback');
          uniforms = allocate(stride * 16, GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST, 'parameters');
          device.queue.writeBuffer(source, 0, snapshot);
          stats.sourceUploads++; stats.uploadedBytes += length;
        });
        sessions.add(api);
        return api;
      } catch (error) { dispose(); throw error; }
    });
  }
  async function applyChain(bytes, width, height, operations) {
    const captured = normalize(operations);
    if (captured.some(op => !kinds.has(op.kind))) return null;
    const session = await createSession(bytes, width, height);
    if (!session) return null;
    try { return await session.apply(captured); } finally { session.dispose(); }
  }
  const from64 = value => { const text = atob(value); return Uint8Array.from(text, c => c.charCodeAt(0)); };
  const to64 = bytes => {
    const chunks = [];
    for (let i = 0; i < bytes.length; i += 32768) chunks.push(String.fromCharCode(...bytes.subarray(i, i + 32768)));
    return btoa(chunks.join(''));
  };
  globalThis.imageSpaceGpu = Object.freeze({
    initialize, createSession, applyChain,
    apply: (bytes, width, height, kind, amount = 0, secondary = 0) => applyChain(bytes, width, height, [{ kind, amount, secondary }]),
    supports: kind => kinds.has(kind),
    describe: () => ({ available: !!current, backend: description, maximumBufferSize: current?.device.limits.maxBufferSize ?? 0,
      residentBytes: reserved, residentBudgetBytes: budget, liveSessions: sessions.size, pipelineCount: current ? 5 : 0 }),
    shaderSource: pointCode,
    createHandle: async (base64, width, height) => {
      const session = await createSession(from64(base64), width, height);
      if (!session) return '';
      const handle = String(nextHandle++); handles.set(handle, session); return handle;
    },
    evaluateHandle: async (handle, json) => {
      const session = handles.get(handle);
      if (!session) throw new Error('Unknown GPU session handle.');
      return to64(await session.apply(JSON.parse(json)));
    },
    releaseHandle: handle => { handles.get(handle)?.dispose(); handles.delete(handle); }
  });
})();
