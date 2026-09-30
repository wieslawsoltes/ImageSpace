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
  // Horizontal tiles keep packed RGBA (3,584 bytes); vertical tiles keep the
  // exact float intermediate (14,336 bytes). Both fit the 16 KiB core baseline.
  // Every lane participates in cooperative loading and the barrier, including
  // out-of-image lanes in partial workgroups. Bounds exits occur AFTER the barrier.
  const tiledHorizontalCode = header + `
@group(0) @binding(0) var<storage,read> input:array<u32>;
@group(0) @binding(1) var<storage,read_write> output:array<vec4f>;
var<workgroup> tile:array<u32,896>;
@compute @workgroup_size(32,4)
fn main(@builtin(workgroup_id) group:vec3u,
        @builtin(local_invocation_id) local:vec3u,
        @builtin(local_invocation_index) lane:u32) {
  let tileWidth=32u+2u*p.radius;
  let left=i32(group.x*32u)-i32(p.radius);
  let top=i32(group.y*4u);
  for (var n=lane;n<tileWidth*4u;n+=128u) {
    tile[n]=input[offset(left+i32(n%tileWidth),top+i32(n/tileWidth))];
  }
  workgroupBarrier();
  let x=group.x*32u+local.x; let y=group.y*4u+local.y;
  if ((x>=p.width)||(y>=p.height)) { return; }
  var sum=vec4f(0.0);
  for (var tap=0u;tap<=2u*p.radius;tap++) {
    let c=decode(tile[local.y*tileWidth+local.x+tap]); let a=c.w/255.0;
    sum+=vec4f(c.xyz*a,a)*weight(i32(tap)-i32(p.radius));
  }
  output[y*p.width+x]=sum;
}`;
  const tiledVerticalCode = header + `
@group(0) @binding(0) var<storage,read> input:array<vec4f>;
@group(0) @binding(1) var<storage,read_write> output:array<u32>;
var<workgroup> tile:array<vec4f,896>;
@compute @workgroup_size(4,32)
fn main(@builtin(workgroup_id) group:vec3u,
        @builtin(local_invocation_id) local:vec3u,
        @builtin(local_invocation_index) lane:u32) {
  let tileHeight=32u+2u*p.radius;
  let left=i32(group.x*4u); let top=i32(group.y*32u)-i32(p.radius);
  for (var n=lane;n<tileHeight*4u;n+=128u) {
    tile[n]=input[offset(left+i32(n%4u),top+i32(n/4u))];
  }
  workgroupBarrier();
  let x=group.x*4u+local.x; let y=group.y*32u+local.y;
  if ((x>=p.width)||(y>=p.height)) { return; }
  var sum=vec4f(0.0);
  for (var tap=0u;tap<=2u*p.radius;tap++) {
    sum+=tile[(local.y+tap)*4u+local.x]*weight(i32(tap)-i32(p.radius));
  }
  var value=0u;
  if (sum.w>0.0) { value=pack(vec4f(sum.xyz/sum.w,sum.w*255.0)); }
  output[y*p.width+x]=value;
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
  const shaderSources = Object.freeze({ point: pointCode, fused: fusedCode,
    horizontal: horizontalCode, vertical: verticalCode, pixelate: pixelateCode,
    tiledHorizontal: tiledHorizontalCode, tiledVertical: tiledVerticalCode });

  async function preparePipeline(runtime, name) {
    if (runtime.pipelines[name]) return;
    const module = runtime.device.createShaderModule({ label: 'ImageSpace ' + name, code: shaderSources[name] });
    const errors = (await module.getCompilationInfo()).messages.filter(message => message.type === 'error');
    if (errors.length) throw new Error(errors.map(e => `${name} ${e.lineNum}:${e.linePos} ${e.message}`).join('\n'));
    runtime.pipelines[name] = await runtime.device.createComputePipelineAsync({
      label: 'ImageSpace ' + name, layout: runtime.pipelineLayout, compute: { module, entryPoint: 'main' }
    });
  }

  function gaussianSigma(amount) {
    if (!Number.isFinite(amount) || amount <= 0 || amount > 1000000)
      throw new RangeError('Gaussian calibration requires a finite positive amount at most 1000000.');
    return Math.max(.1, Math.min(32, amount));
  }

  function calibrationOptions(options) {
    if (!options || typeof options !== 'object' || Array.isArray(options))
      throw new TypeError('Expected Gaussian calibration options.');
    const samples = options.samples ?? 5, warmups = options.warmups ?? 1;
    if (!Number.isInteger(samples) || samples < 3 || samples > 9 || samples % 2 !== 1 ||
        !Number.isInteger(warmups) || warmups < 1 || warmups > 3)
      throw new RangeError('Calibration requires 3, 5, 7 or 9 samples and 1–3 warmups.');
    if (options.force !== undefined && typeof options.force !== 'boolean')
      throw new TypeError('force must be a boolean.');
    const signal = options.signal;
    if (signal !== undefined && (!signal || typeof signal.aborted !== 'boolean' ||
        typeof signal.addEventListener !== 'function')) throw new TypeError('signal must be an AbortSignal.');
    return { samples, warmups, force: options.force === true, signal };
  }

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
          const runtime = { device, layout, pipelineLayout, pipelines: {}, generation: ++generation };
          // Color/direct processing must not pay for workgroup-tile shader compilation.
          // Optional tiled pipelines are compiled on first explicit use or calibration.
          for (const name of ['point', 'fused', 'horizontal', 'vertical', 'pixelate'])
            await preparePipeline(runtime, name);
          return runtime;
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
    if (options.gaussianBlur !== undefined && !['auto', 'direct', 'tiled'].includes(options.gaussianBlur))
      throw new TypeError('gaussianBlur must be auto, direct or tiled.');
    const blurStrategy = options.gaussianBlur ?? 'auto';
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
      const resources = [], bindings = new Map(), calibrations = new Map();
      // Bounded host-side preparation caches. They do not hold output pixels or
      // bypass queue execution; every evaluation still starts from immutable source.
      const executionPlans = new Map(), gaussianWeights = new Map();
      const maximumCachedPlans = 8, maximumCachedWeights = 8;
      let cachedParameterBytes = 0;
      let closed = false, scratch = null, last = null, owned = 0;
      const stats = { sourceUploads: 0, uploadedBytes: 0, submissions: 0, dispatches: 0, readbacks: 0, bufferAllocations: 0, bindGroupBuilds: 0, logicalOperations: 0, fusedPasses: 0, parameterBytesUploaded: 0, tiledGaussianPasses: 0, directGaussianPasses: 0, gaussianInputReads: 0, gaussianCalibrations: 0, gaussianCalibrationHits: 0, executionPlanBuilds: 0, executionPlanHits: 0, gaussianWeightBuilds: 0, gaussianWeightHits: 0 };
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
      const clearExecutionPlans = () => {
        executionPlans.clear(); cachedParameterBytes = 0;
      };
      const dispose = () => {
        if (closed) return;
        closed = true;
        for (const buffer of resources) buffer.destroy();
        reserved -= owned; owned = 0; resources.length = 0; bindings.clear(); calibrations.clear(); sessions.delete(api); last = null;
        clearExecutionPlans(); gaussianWeights.clear();
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
      // Calibration compares a Gaussian applied to this session's original pixels.
      // A Gaussian after another stage has a different input: do not extrapolate a
      // measured equivalence/timing profile to that uncalibrated workload.
      const strategyFor = (sigma, originalSource = true) => blurStrategy === 'auto'
        ? (originalSource ? calibrations.get(sigma)?.selected ?? 'direct' : 'direct') : blurStrategy;
      const weightsFor = sigma => {
        const cached = gaussianWeights.get(sigma);
        if (cached) {
          gaussianWeights.delete(sigma); gaussianWeights.set(sigma, cached);
          stats.gaussianWeightHits++;
          return cached;
        }
        const radius = Math.ceil(sigma * 3), values = [];
        let total = 0;
        for (let i = -radius; i <= radius; i++) {
          const value = Math.exp(-i * i / (2 * sigma * sigma));
          values.push(value); total += value;
        }
        // Normalize in double, round to float once exactly as the uncached writer.
        const coefficients = Float32Array.from(values, value => value / total);
        const result = { radius, coefficients };
        if (gaussianWeights.size >= maximumCachedWeights)
          gaussianWeights.delete(gaussianWeights.keys().next().value);
        gaussianWeights.set(sigma, result); stats.gaussianWeightBuilds++;
        return result;
      };
      const prepareTiled = async () => {
        await preparePipeline(runtime, 'tiledHorizontal');
        await preparePipeline(runtime, 'tiledVertical');
        ensure();
      };
      const prepareExecutionPlan = (operations, gaussianOverride) => {
        const key = JSON.stringify([gaussianOverride ?? null, operations]);
        const cached = executionPlans.get(key);
        if (cached) {
          executionPlans.delete(key); executionPlans.set(key, cached);
          stats.executionPlanHits++;
          return cached;
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
            const sigma = Math.max(.1, Math.min(32, op.amount));
            const { radius, coefficients } = weightsFor(sigma);
            integers[3] = radius; floats.set(coefficients, 8);
            // A lower shader-load count is not proof of lower latency. Untuned
            // automatic sessions retain the direct reference on EVERY adapter.
            const tiled = (gaussianOverride ?? strategyFor(sigma, input === source)) === 'tiled';
            plan.push({ name: tiled ? 'tiledHorizontal' : 'horizontal', input, output: scratch, slot, radius });
            plan.push({ name: tiled ? 'tiledVertical' : 'vertical', input: scratch, output, slot, radius });
          } else plan.push({ name: op.kind === 'Pixelate' ? 'pixelate' : 'point', input, output, slot, size: integers[6] });
          input = output; slot++; index = end;
        }
        // GPUQueue.writeBuffer snapshots these bytes at call time. Keep one private
        // template per plan instead of allocating descriptors/coefficients every run.
        const entry = { plan, output: input, parameters: parameterBytes.slice(0, slot * stride) };
        if (executionPlans.size >= maximumCachedPlans) {
          const oldest = executionPlans.keys().next().value;
          cachedParameterBytes -= executionPlans.get(oldest).parameters.byteLength;
          executionPlans.delete(oldest);
        }
        executionPlans.set(key, entry); cachedParameterBytes += entry.parameters.byteLength;
        stats.executionPlanBuilds++;
        return entry;
      };
      const dispatch = (operations, gaussianOverride) => {
        ensure();
        if (operations.some(op => !kinds.has(op.kind))) throw new Error('Unsupported filter in the resident stack.');
        if (operations.some(op => op.kind === 'GaussianBlur') && !scratch) {
          if (length * 4 > device.limits.maxStorageBufferBindingSize) throw new RangeError('Blur scratch exceeds the adapter storage-binding limit.');
          scratch = allocate(length * 4, GPUBufferUsage.STORAGE, 'premultiplied blur scratch');
        }
        if (!operations.length) { last = source; return; }
        const cached = prepareExecutionPlan(operations, gaussianOverride);
        device.queue.writeBuffer(uniforms, 0, cached.parameters);
        stats.parameterBytesUploaded += cached.parameters.byteLength;
        const encoder = device.createCommandEncoder({ label: 'ImageSpace resident filter stack' });
        for (const op of cached.plan) {
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
          else if (op.name === 'tiledHorizontal' || op.name === 'tiledVertical') {
            const horizontal = op.name === 'tiledHorizontal';
            const groupsX = Math.ceil(width / (horizontal ? 32 : 4));
            const groupsY = Math.ceil(height / (horizontal ? 4 : 32));
            pass.dispatchWorkgroups(groupsX, groupsY);
            stats.tiledGaussianPasses++;
            // Algorithmic shader input loads, INCLUDING clamped halo/tail loads.
            // This is not a hardware counter or a count of physical DRAM accesses.
            stats.gaussianInputReads += groupsX * groupsY * (32 + 2 * op.radius) * 4;
          } else {
            pass.dispatchWorkgroups(Math.ceil(width / 8), Math.ceil(height / 8));
            if (op.name === 'horizontal' || op.name === 'vertical') {
              stats.directGaussianPasses++;
              stats.gaussianInputReads += width * height * (2 * op.radius + 1);
            }
          }
          pass.end(); stats.dispatches++;
          if (op.name === 'fused') stats.fusedPasses++;
        }
        device.queue.submit([encoder.finish()]); stats.submissions++; stats.logicalOperations += operations.length; last = cached.output;
      };
      const run = (operations, read) => {
        const captured = normalize(operations);
        return exclusive(async () => {
          ensure();
          try {
            return await scoped(device, async () => {
              if (captured.some((op, index) => op.kind === 'GaussianBlur' && strategyFor(gaussianSigma(op.amount), index === 0) === 'tiled'))
                await prepareTiled();
              dispatch(captured);
              return read ? await readOutput() : undefined;
            });
          }
          catch (error) { dispose(); throw error; }
        });
      };
      const calibrateGaussian = (amount, options = {}) => {
        const sigma = gaussianSigma(amount);
        const { samples, warmups, force, signal } = calibrationOptions(options);
        const check = () => {
          ensure();
          if (signal?.aborted) throw new DOMException('Gaussian calibration was cancelled.', 'AbortError');
        };
        return exclusive(async () => {
          check();
          const cached = calibrations.get(sigma);
          if (cached && !force) { stats.gaussianCalibrationHits++; return cached; }
          let preserved = last;
          try {
            return await scoped(device, async () => {
              await prepareTiled();
              check();
              // A single Gaussian always targets ping[0]. Protect the currently
              // published result using the already-owned, otherwise unused ping[1].
              // Calibration neither uploads source again nor allocates a backup.
              if (last === ping[0]) {
                const encoder = device.createCommandEncoder({ label: 'ImageSpace preserve calibration output' });
                encoder.copyBufferToBuffer(ping[0], 0, ping[1], 0, length);
                device.queue.submit([encoder.finish()]); stats.submissions++;
                preserved = ping[1];
              }
              const recipe = [{ kind: 'GaussianBlur', amount: sigma, secondary: 0, enabled: true }];
              const complete = async () => {
                let timeout;
                try {
                  await Promise.race([device.queue.onSubmittedWorkDone(), new Promise((_, reject) => {
                    timeout = setTimeout(() => reject(new Error('Gaussian calibration timed out.')), 30000);
                  })]);
                  check();
                } finally { clearTimeout(timeout); }
              };
              // Compare actual output before allowing the alternative path. Readback
              // costs are excluded from the timing samples, but counted honestly.
              dispatch(recipe, 'direct');
              const reference = await readOutput(); check();
              dispatch(recipe, 'tiled');
              const candidate = await readOutput(); check();
              let maximumByteDifference = 0;
              for (let i = 0; i < length; i++)
                maximumByteDifference = Math.max(maximumByteDifference, Math.abs(reference[i] - candidate[i]));
              for (let i = 0; i < warmups; i++) {
                dispatch(recipe, 'direct'); await complete();
                dispatch(recipe, 'tiled'); await complete();
              }
              const directMilliseconds = [], tiledMilliseconds = [];
              const measure = async (mode, times) => {
                check();
                const started = performance.now();
                dispatch(recipe, mode); await complete();
                times.push(performance.now() - started);
              };
              for (let i = 0; i < samples; i++) {
                if (i % 2 === 0) {
                  await measure('direct', directMilliseconds); await measure('tiled', tiledMilliseconds);
                } else {
                  await measure('tiled', tiledMilliseconds); await measure('direct', directMilliseconds);
                }
              }
              check();
              const median = values => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];
              const directMedian = median(directMilliseconds), tiledMedian = median(tiledMilliseconds);
              const minimumSpeedup = 1.15;
              const tiledWins = tiledMilliseconds.filter((time, i) => time < directMilliseconds[i]).length;
              const equivalent = maximumByteDifference === 0;
              const faster = directMedian > 0 && tiledMedian > 0 &&
                directMedian / tiledMedian >= minimumSpeedup && tiledWins >= Math.ceil(samples * .7);
              const report = Object.freeze({ width, height, sigma, generation: runtime.generation,
                selected: equivalent && faster ? 'tiled' : 'direct', equivalent, maximumByteDifference,
                minimumSpeedup, tiledWins, samples, warmups, directMedian, tiledMedian,
                directMilliseconds: Object.freeze(directMilliseconds), tiledMilliseconds: Object.freeze(tiledMilliseconds),
                scope: 'Same-session warm queue-completion wall time: parameter upload, command encoding and GPU completion. Excludes shader compilation and correctness readback; not timestamp-query GPU time.' });
              // Bound profile storage; source dimensions and device generation belong
              // to this session. A different sigma starts on direct until calibrated.
              if (calibrations.has(sigma)) calibrations.delete(sigma);
              if (calibrations.size >= 8) calibrations.delete(calibrations.keys().next().value);
              calibrations.set(sigma, report); stats.gaussianCalibrations++;
              // Plan strategy is frozen at preparation. Invalidate cached decisions
              // whenever profiles change, including forced rechecks and evictions.
              clearExecutionPlans();
              return report;
            });
          } catch (error) {
            // Cancellation cannot preempt submitted work. The next queued operation
            // remains ordered behind it and observes only the preserved prior result.
            if (error?.name !== 'AbortError') dispose();
            throw error;
          } finally { if (!closed) last = preserved; }
        });
      };
      const api = Object.freeze({
        width, height, generation: runtime.generation,
        calibrateGaussian,
        gaussianCalibration: amount => { ensure(); return calibrations.get(gaussianSigma(amount)) ?? null; },
        clearGaussianCalibrations: () => exclusive(() => { ensure(); calibrations.clear(); clearExecutionPlans(); }),
        apply: operations => run(operations, true), execute: operations => run(operations, false),
        read: () => exclusive(async () => {
          ensure();
          try { return await scoped(device, readOutput); } catch (error) { dispose(); throw error; }
        }),
        statistics: () => ({ ...stats, residentBytes: owned, disposed: closed,
          executionPlansCached: executionPlans.size, gaussianWeightsCached: gaussianWeights.size,
          cachedParameterBytes }), dispose
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
      residentBytes: reserved, residentBudgetBytes: budget, liveSessions: sessions.size, pipelineCount: current ? Object.keys(current.pipelines).length : 0 }),
    shaderSource: pointCode, shaderSources,
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
