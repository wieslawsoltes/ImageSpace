import {test,expect} from '@playwright/test';
import {mkdir,writeFile} from 'node:fs/promises';

test('resident stack matches individual filters and records same-device A/B completion timings',async({page})=>{
  await page.goto('/');await page.addScriptTag({url:'/WebGpu.js'});
  const result=await page.evaluate(async()=>{
    const width=256,height=256,source=new Uint8Array(width*height*4);
    for(let y=0;y<height;y++)for(let x=0;x<width;x++)source.set([(x*7+y*11)%256,(x*13+y*3)%256,(x*3+y*17)%256,255],(y*width+x)*4);
    const operations=[{kind:'GaussianBlur',amount:2},{kind:'Saturation',amount:-35},{kind:'Sharpen',amount:.3},{kind:'Gamma',amount:1.25}];
    const session=await imageSpaceGpu.createSession(source,width,height);
    if(!session)throw new Error(imageSpaceGpu.describe().backend);
    async function individual(){
      let bytes=source;
      for(const op of operations){bytes=await imageSpaceGpu.apply(bytes,width,height,op.kind,op.amount,op.secondary);if(!bytes)throw new Error('Missing filter result');}
      return bytes;
    }
    const a=[],b=[];
    try{
      const reference=await individual(),actual=await session.apply(operations);
      let maximumDifference=0;for(let i=0;i<actual.length;i++)maximumDifference=Math.max(maximumDifference,Math.abs(actual[i]-reference[i]));
      await individual();await session.apply(operations);const warm=session.statistics();
      async function time(fn,results){const t=performance.now();await fn();results.push(performance.now()-t);}
      for(let i=0;i<7;i++){
        if(i%2===0){await time(individual,a);await time(()=>session.apply(operations),b);}
        else{await time(()=>session.apply(operations),b);await time(individual,a);}
      }
      const median=values=>[...values].sort((x,y)=>x-y)[Math.floor(values.length/2)];
      return {adapter:imageSpaceGpu.describe().backend,width,height,operations,warmups:2,samples:7,
        individualMilliseconds:a,residentMilliseconds:b,individualMedian:median(a),residentMedian:median(b),ratio:median(a)/median(b),
        maximumDifference,warm,final:session.statistics(),
        scope:'Same-device wall-clock API completion including allocation/upload/readback for individual calls. Warm resident session. Software adapters are allowed. Not hardware GPU timing or whole-editor latency.'};
    }finally{session.dispose();}
  });
  expect(result.maximumDifference).toBe(0);
  expect(result.final.sourceUploads).toBe(1);expect(result.final.bufferAllocations).toBe(result.warm.bufferAllocations);
  expect(result.final.bindGroupBuilds).toBe(result.warm.bindGroupBuilds);
  expect(await page.evaluate(()=>imageSpaceGpu.describe().residentBytes)).toBe(0);
  await mkdir('artifacts',{recursive:true});await writeFile('artifacts/resident-gpu-ab-performance.json',JSON.stringify(result,null,2));
  console.log('RESIDENT_GPU_AB '+JSON.stringify(result));
});
