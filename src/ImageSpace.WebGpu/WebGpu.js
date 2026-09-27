/* ImageSpace WebGPU compute backend. MIT. This file also runs independently of Uno. */
(() => {
  'use strict';
  let device = null, pipeline = null, initializing = null, description = 'Not initialized';
  const kinds = new Map(['Invert','Grayscale','Sepia','BrightnessContrast','Saturation','Gamma','Threshold','Posterize'].map((k,i)=>[k,i]));
  const code = `
struct Parameters { width:u32, height:u32, kind:u32, reserved:u32, amount:f32, secondary:f32, padding:vec2f }
@group(0) @binding(0) var<storage,read> input:array<u32>;
@group(0) @binding(1) var<storage,read_write> output:array<u32>;
@group(0) @binding(2) var<uniform> params:Parameters;
@compute @workgroup_size(16,16)
fn main(@builtin(global_invocation_id) id:vec3u) {
  if (id.x >= params.width || id.y >= params.height) { return; }
  let i=id.y*params.width+id.x;
  let packed=input[i];
  let c=vec3f(f32(packed & 255u),f32((packed>>8u)&255u),f32((packed>>16u)&255u))/255.0;
  let alpha=packed & 0xff000000u;
  if (alpha==0u) { output[i]=0u; return; }
  let l=dot(c,vec3f(0.2126,0.7152,0.0722));
  var rgb=c;
  switch params.kind {
    case 0u: { rgb=vec3f(1.0)-c; }
    case 1u: { rgb=vec3f(l); }
    case 2u: { rgb=vec3f(dot(c,vec3f(0.393,0.769,0.189)),dot(c,vec3f(0.349,0.686,0.168)),dot(c,vec3f(0.272,0.534,0.131))); }
    case 3u: { let contrast=1.0+clamp(params.secondary,-99.0,300.0)/100.0; rgb=(c-vec3f(0.5))*contrast+vec3f(0.5+params.amount/100.0); }
    case 4u: { rgb=vec3f(l)+(c-vec3f(l))*max(0.0,1.0+params.amount/100.0); }
    case 5u: { rgb=pow(c,vec3f(1.0/clamp(params.amount,0.1,10.0))); }
    case 6u: { rgb=select(vec3f(0.0),vec3f(1.0),l*255.0>=clamp(params.amount,0.0,255.0)); }
    case 7u: { let levels=f32(clamp(i32(params.amount),2,256)-1); rgb=round(c*levels)/levels; }
    default: {}
  }
  let bytes=vec3u(round(clamp(rgb,vec3f(0.0),vec3f(1.0))*255.0));
  output[i]=bytes.x|(bytes.y<<8u)|(bytes.z<<16u)|alpha;
}`;
  async function initialize() {
    if (device && pipeline) return device;
    if (initializing) return initializing;
    initializing = (async()=> {
      if (!navigator.gpu) { description='WebGPU unavailable'; return null; }
      const adapter=await navigator.gpu.requestAdapter({powerPreference:'high-performance'});
      if (!adapter) { description='No WebGPU adapter'; return null; }
      const next=await adapter.requestDevice();
      next.pushErrorScope('validation');
      const module=next.createShaderModule({label:'ImageSpace color kernels',code});
      const info=await module.getCompilationInfo();
      const errors=info.messages.filter(m=>m.type==='error');
      if(errors.length) throw new Error(errors.map(m=>`${m.lineNum}:${m.linePos} ${m.message}`).join('\n'));
      const nextPipeline=await next.createComputePipelineAsync({label:'ImageSpace color filters',layout:'auto',compute:{module,entryPoint:'main'}});
      const error=await next.popErrorScope();if(error)throw new Error(error.message);
      device=next;pipeline=nextPipeline;
      description='WebGPU · '+(adapter.info?.description||adapter.info?.architecture||adapter.info?.vendor||'compatible adapter');
      next.lost.then(info=>{if(device===next){device=null;pipeline=null;description='Device lost: '+info.message;}});
      return next;
    })().catch(error=>{description='WebGPU initialization failed: '+error.message;device=null;pipeline=null;return null;}).finally(()=>{initializing=null;});
    return initializing;
  }
  async function apply(bytes,width,height,kind,amount=0,secondary=0) {
    if(!kinds.has(kind))return null;
    if(!Number.isInteger(width)||!Number.isInteger(height)||width<1||height<1||width>8192||height>8192||width*height>16777216||bytes.byteLength!==width*height*4)throw new Error('Invalid image dimensions or RGBA length.');
    if(!Number.isFinite(amount)||!Number.isFinite(secondary))throw new Error('Filter parameters must be finite.');
    const gpu=await initialize();if(!gpu)return null;
    const length=bytes.byteLength;if(length>gpu.limits.maxStorageBufferBindingSize||length>gpu.limits.maxBufferSize)return null;
    const resources=[];let timer;
    const create=(size,usage,label)=>{const b=gpu.createBuffer({size,usage,label});resources.push(b);return b;};
    gpu.pushErrorScope('validation');
    try {
      const input=create(length,GPUBufferUsage.STORAGE|GPUBufferUsage.COPY_DST,'ImageSpace input');
      const output=create(length,GPUBufferUsage.STORAGE|GPUBufferUsage.COPY_SRC,'ImageSpace output');
      const uniform=create(32,GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST,'ImageSpace parameters');
      const readback=create(length,GPUBufferUsage.MAP_READ|GPUBufferUsage.COPY_DST,'ImageSpace readback');
      const params=new ArrayBuffer(32);new Uint32Array(params).set([width,height,kinds.get(kind),0]);new Float32Array(params,16,2).set([amount,secondary]);
      gpu.queue.writeBuffer(input,0,bytes);gpu.queue.writeBuffer(uniform,0,params);
      const bind=gpu.createBindGroup({layout:pipeline.getBindGroupLayout(0),entries:[{binding:0,resource:{buffer:input}},{binding:1,resource:{buffer:output}},{binding:2,resource:{buffer:uniform}}]});
      const encoder=gpu.createCommandEncoder({label:'ImageSpace filter'});const pass=encoder.beginComputePass();pass.setPipeline(pipeline);pass.setBindGroup(0,bind);pass.dispatchWorkgroups(Math.ceil(width/16),Math.ceil(height/16));pass.end();encoder.copyBufferToBuffer(output,0,readback,0,length);gpu.queue.submit([encoder.finish()]);
      await Promise.race([readback.mapAsync(GPUMapMode.READ),new Promise((_,reject)=>{timer=setTimeout(()=>reject(new Error('GPU readback timed out.')),30000);})]);
      clearTimeout(timer);const result=new Uint8Array(readback.getMappedRange()).slice();readback.unmap();
      const error=await gpu.popErrorScope();if(error)throw new Error(error.message);return result;
    } catch(error) {try{await gpu.popErrorScope();}catch{}throw error;}
    finally {clearTimeout(timer);for(const resource of resources)resource.destroy();}
  }
  globalThis.imageSpaceGpu={initialize,apply,supports:kind=>kinds.has(kind),describe:()=>({available:!!device&&!!pipeline,backend:description,maximumBufferSize:device?.limits.maxBufferSize||0}),shaderSource:code};
})();
