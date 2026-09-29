import {test,expect} from '@playwright/test';
import {deflateSync} from 'node:zlib';
import {mkdir} from 'node:fs/promises';
import {waitForWorkspace} from './readiness.mjs';
import {waitForControl as control} from './control.mjs';
import {decodePng} from './png.mjs';

// Independent RGBA PNG fixture. No JavaScript endpoint mutates the C# model.
function translucentPng() {
  function chunk(name,bytes) {
    const tag=Buffer.from(name),body=Buffer.concat([tag,bytes]);
    let crc=0xffffffff;
    for(const byte of body) {crc^=byte;for(let b=0;b<8;b++)crc=(crc>>>1)^((crc&1)?0xedb88320:0);}
    const header=Buffer.alloc(4),tail=Buffer.alloc(4);
    header.writeUInt32BE(bytes.length);tail.writeUInt32BE((crc^0xffffffff)>>>0);
    return Buffer.concat([header,body,tail]);
  }
  const header=Buffer.alloc(13);header.writeUInt32BE(64,0);header.writeUInt32BE(64,4);header[8]=8;header[9]=6;
  const scanlines=Buffer.alloc(64*(1+64*4));
  for(let y=0;y<64;y++)for(let x=0;x<64;x++)scanlines.set([54,145,230,128],y*(1+64*4)+1+x*4);
  return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),chunk('IHDR',header),chunk('IDAT',deflateSync(scanlines)),chunk('IEND',Buffer.alloc(0))]);
}
const state=page=>page.evaluate(()=>globalThis.imageSpaceDiagnostics);
async function click(page,name) {const c=await control(page,name);await page.mouse.click(c.x+c.width/2,c.y+c.height/2);}

test('before-after comparison composites transparent source once and leaves editing state unchanged',async({page})=>{
  await page.goto('./?test=1',{waitUntil:'domcontentloaded'});await waitForWorkspace(page);
  await click(page,'File');const choosing=page.waitForEvent('filechooser');await click(page,'Open…');
  await(await choosing).setFiles({name:'Translucent-preview.png',mimeType:'image/png',buffer:translucentPng()});
  await expect.poll(async()=>(await state(page)).documents).toBe(2);
  const before=await state(page);
  await click(page,'Filter');await click(page,'Filter Gallery…');await control(page,'Filter Gallery preview ready');
  const view=await control(page,'Filter Gallery preview');
  const p={x:view.x+view.width*.25,y:view.y+view.height*.5};
  const read=async()=>decodePng(await page.screenshot()).pixel(p.x,p.y).slice(0,3);
  await page.mouse.move(10,10);
  await expect.poll(async()=>{const rgb=await read();return Math.max(...rgb)-Math.min(...rgb);}).toBe(0);
  const filtered=await read();
  const gpuBefore=await page.evaluate(()=>imageSpaceGpu.describe());
  await click(page,'Filter Gallery comparison');await page.keyboard.press('End');await page.mouse.move(10,10);
  const expected=[145,190].map(background=>[54,145,230].map(channel=>Math.round((channel*128+background*127)/255)));
  await expect.poll(async()=>{
    const rgb=await read();return Math.min(...expected.map(color=>Math.max(...color.map((v,i)=>Math.abs(v-rgb[i])))));
  },{message:'The original must blend once with its checkerboard, never over the filtered image.'}).toBeLessThanOrEqual(1);
  const gpuAfter=await page.evaluate(()=>imageSpaceGpu.describe());
  expect(gpuAfter.residentBytes).toBe(gpuBefore.residentBytes);
  expect((await state(page)).history).toBe(before.history);
  await click(page,'Filter Gallery comparison');await page.keyboard.press('Home');await page.mouse.move(10,10);
  await expect.poll(read).toEqual(filtered);
  await mkdir('artifacts/screenshots',{recursive:true});
  await page.screenshot({path:'artifacts/screenshots/filter-gallery-transparency.png'});
  await click(page,'Cancel');await expect.poll(async()=>(await state(page)).dialog).toBe(false);
  await expect.poll(()=>page.evaluate(()=>imageSpaceGpu.describe().liveSessions)).toBe(0);
  expect((await state(page)).contentRevision).toBe(before.contentRevision);
});
