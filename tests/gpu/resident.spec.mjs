import {test,expect} from '@playwright/test';
import {mkdir,writeFile} from 'node:fs/promises';
import {reference,fixture} from './reference.mjs';

const operations=[
  {kind:'Invert'},{kind:'Grayscale'},{kind:'Sepia'},
  {kind:'BrightnessContrast',amount:12,secondary:23},{kind:'Saturation',amount:-65},
  {kind:'Gamma',amount:1.7},{kind:'Threshold',amount:103},{kind:'Posterize',amount:6},
  {kind:'GaussianBlur',amount:2.3},{kind:'Sharpen',amount:.75},{kind:'Emboss'},{kind:'Edges'},{kind:'Pixelate',amount:7}
];
test.beforeEach(async({page})=>{
  await page.goto('/');await page.addScriptTag({url:'/WebGpu.js'});
  const available=await page.evaluate(async()=>{await imageSpaceGpu.initialize();return imageSpaceGpu.describe();});
  // This workflow deliberately requires a functioning adapter, not a skipped success.
  expect(available.available,available.backend).toBe(true);
});
function compare(expected,actual,tolerance=1){
  expect(actual.length).toBe(expected.length);let worst=0;
  for(let i=0;i<expected.length;i++){
    if(i%4!==3&&expected[i-i%4+3]===0&&actual[i-i%4+3]===0)continue;
    worst=Math.max(worst,Math.abs(expected[i]-actual[i]));
  }
  expect(worst).toBeLessThanOrEqual(tolerance);
}
for(const shape of [[19,17],[257,3],[1,1]])test(`all thirteen kernels match independent reference at ${shape.join('x')}`,async({page})=>{
  const [width,height]=shape,source=fixture(width,height);
  for(const operation of operations){
    const result=await page.evaluate(async({source,width,height,operation})=>Array.from(await imageSpaceGpu.apply(new Uint8Array(source),width,height,operation.kind,operation.amount,operation.secondary)),{source:Array.from(source),width,height,operation});
    compare(reference(source,width,height,operation),result);
  }
});
test('wide blur and 128px block reduction preserve premultiplied edge coverage',async({page})=>{
  const width=131,height=7,source=fixture(width,height);
  for(const operation of [{kind:'GaussianBlur',amount:32},{kind:'Pixelate',amount:128},{kind:'GaussianBlur',amount:0}]){
    const result=await page.evaluate(async p=>Array.from(await imageSpaceGpu.apply(new Uint8Array(p.bytes),p.width,p.height,p.op.kind,p.op.amount)),{bytes:Array.from(source),width,height,op:operation});
    compare(reference(source,width,height,operation),result);
  }
});
test('ordered resident stacks read once, reset to immutable source and reuse resources',async({page})=>{
  const width=19,height=17,source=fixture(width,height),stack=[operations[9],operations[8],operations[3],operations[12]];
  const result=await page.evaluate(async({bytes,width,height,stack})=>{
    const session=await imageSpaceGpu.createSession(new Uint8Array(bytes),width,height);
    try{
      await session.execute(stack);const executed=session.statistics();
      const first=Array.from(await session.read());const warm=session.statistics();
      const second=Array.from(await session.apply(stack));const final=session.statistics();
      const reset=Array.from(await session.apply([]));
      return {first,second,reset,executed,warm,final};
    }finally{session.dispose();}
  },{bytes:Array.from(source),width,height,stack});
  let expected=source;for(const op of stack)expected=reference(expected,width,height,op);
  compare(expected,result.first,2);expect(result.second).toEqual(result.first);expect(result.reset).toEqual(Array.from(source));
  expect(result.executed.readbacks).toBe(0);expect(result.warm.readbacks).toBe(1);
  expect(result.final.sourceUploads).toBe(1);expect(result.final.uploadedBytes).toBe(source.length);
  expect(result.final.bufferAllocations).toBe(result.warm.bufferAllocations);
  expect(result.final.bindGroupBuilds).toBe(result.warm.bindGroupBuilds);
  expect(await page.evaluate(()=>imageSpaceGpu.describe().residentBytes)).toBe(0);
});
test('concurrent independent sessions and jobs serialize without error-scope interference',async({page})=>{
  const result=await page.evaluate(async()=>{
    const bytes=new Uint8Array([10,20,30,255]);
    const [a,b]=await Promise.all([imageSpaceGpu.createSession(bytes,1,1),imageSpaceGpu.createSession(bytes,1,1)]);
    bytes.fill(255);
    try{return await Promise.all([a.apply([{kind:'Invert'}]),b.apply([{kind:'Gamma',amount:1}]),a.apply([])]).then(outputs=>outputs.map(Array.from.bind(Array)));}
    finally{a.dispose();b.dispose();}
  });
  expect(result).toEqual([[245,235,225,255],[10,20,30,255],[10,20,30,255]]);
});
test('disposed sessions, bad parameters and unsupported kernels fail explicitly without leaks',async({page})=>{
  const result=await page.evaluate(async()=>{
    const errors=[];const fails=async(name,fn)=>{try{await fn();errors.push(name+':accepted');}catch{errors.push(name+':rejected');}};
    await fails('dimensions',()=>imageSpaceGpu.createSession(new Uint8Array(3),1,1));
    await fails('amount',()=>imageSpaceGpu.apply(new Uint8Array(4),1,1,'Gamma',NaN));
    await fails('stack-limit',()=>imageSpaceGpu.applyChain(new Uint8Array(4),1,1,Array(17).fill({kind:'Invert'})));
    const unsupported=await imageSpaceGpu.apply(new Uint8Array(4),1,1,'Noise');
    const session=await imageSpaceGpu.createSession(new Uint8Array(4),1,1);session.dispose();session.dispose();
    await fails('disposed',()=>session.read());
    return {errors,unsupported,state:imageSpaceGpu.describe()};
  });
  expect(result.errors).toEqual(['dimensions:rejected','amount:rejected','stack-limit:rejected','disposed:rejected']);
  expect(result.unsupported).toBeNull();expect(result.state.liveSessions).toBe(0);expect(result.state.residentBytes).toBe(0);
});
test('byte-offset views and disabled operations preserve exact source bytes',async({page})=>{
  const result=await page.evaluate(async()=>{
    const buffer=new Uint8Array([0,0,0,0,13,29,61,255,0,0,0,0]);
    const session=await imageSpaceGpu.createSession(buffer.subarray(4,8),1,1);
    try{return Array.from(await session.apply([{kind:'Invert',enabled:false}]));}finally{session.dispose();}
  });
  expect(result).toEqual([13,29,61,255]);
});
test('device loss invalidates residency and a replacement device rebuilds cleanly',async({page})=>{
  await page.evaluate(async()=>{
    const device=await imageSpaceGpu.initialize();globalThis.oldSession=await imageSpaceGpu.createSession(new Uint8Array(4),1,1);device.destroy();await device.lost;
  });
  await expect.poll(()=>page.evaluate(()=>imageSpaceGpu.describe().residentBytes)).toBe(0);
  const result=await page.evaluate(async()=>{
    let rejected=false;try{await oldSession.read();}catch{rejected=true;}
    const output=await imageSpaceGpu.apply(new Uint8Array([10,20,30,255]),1,1,'Invert');
    return {rejected,output:Array.from(output)};
  });
  expect(result).toEqual({rejected:true,output:[245,235,225,255]});
});
test('record resident transfer counters and explicitly scoped end-to-end timings',async({page})=>{
  const report=await page.evaluate(async()=>{
    const width=512,height=512,bytes=new Uint8Array(width*height*4);for(let i=0;i<bytes.length;i+=4)bytes.set([61,117,183,255],i);
    const ops=[{kind:'Grayscale'},{kind:'Gamma',amount:1.2},{kind:'Sepia'},{kind:'BrightnessContrast',amount:5,secondary:10}];
    const session=await imageSpaceGpu.createSession(bytes,width,height);const samples=[];
    try{
      await session.apply(ops);const warm=session.statistics();
      for(let i=0;i<5;i++){const t=performance.now();await session.apply(ops);samples.push(performance.now()-t);}
      return {adapter:imageSpaceGpu.describe().backend,width,height,operations:ops.length,warm,final:session.statistics(),samples,
        scope:'Wall-clock dispatch, completion and final RGBA readback after warmup. Software adapter permitted; not physical GPU timing or UI frame latency.'};
    }finally{session.dispose();}
  });
  expect(report.final.bufferAllocations).toBe(report.warm.bufferAllocations);
  expect(report.final.sourceUploads).toBe(1);expect(report.final.readbacks).toBe(6);expect(report.final.dispatches).toBe(24);
  await mkdir('artifacts',{recursive:true});await writeFile('artifacts/resident-gpu-performance.json',JSON.stringify(report,null,2));
  console.log('RESIDENT_GPU_REPORT '+JSON.stringify(report));
});
