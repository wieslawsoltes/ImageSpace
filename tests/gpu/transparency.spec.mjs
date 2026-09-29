import {test,expect} from '@playwright/test';
import {readFile} from 'node:fs/promises';

test('spatial stacks retain hidden RGB and match C# across transparent boundaries',async({page})=>{
  const data=JSON.parse(await readFile('artifacts/transparent-filter-fixtures.json','utf8'));
  await page.goto('/');await page.addScriptTag({url:'/WebGpu.js'});
  const result=await page.evaluate(async input=>{
    const source=Uint8Array.from(atob(input.source),c=>c.charCodeAt(0));
    const session=await imageSpaceGpu.createSession(source,input.width,input.height);
    if(!session)throw new Error(imageSpaceGpu.describe().backend);
    try{
      const outputs=[];
      for(const chain of input.chains)outputs.push(Array.from(await session.apply(chain.operations)));
      return outputs;
    }finally{session.dispose();}
  },data);
  for(let i=0;i<data.chains.length;i++){
    const expected=Buffer.from(data.chains[i].expected,'base64'),actual=result[i];
    const error=Math.max(...Array.from(expected,(value,index)=>Math.abs(value-actual[index])));
    // Integer convolution pairs are exact, including fully transparent RGB.
    // Floating blur/block means permit bounded rounding at intermediate stages.
    expect(error,JSON.stringify(data.chains[i].operations)).toBeLessThanOrEqual(i<2?0:3);
  }
  expect(await page.evaluate(()=>imageSpaceGpu.describe().liveSessions)).toBe(0);
});
