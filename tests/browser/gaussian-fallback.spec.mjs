import {test,expect} from '@playwright/test';
import {mkdir} from 'node:fs/promises';
import {waitForWorkspace} from './readiness.mjs';
import {waitForControl} from './control.mjs';
import {decodePng} from './png.mjs';

const state=page=>page.evaluate(()=>globalThis.imageSpaceDiagnostics);
async function click(page,name){
  const control=await waitForControl(page,name);
  await page.mouse.click(control.x+control.width/2,control.y+control.height/2);
}
async function menu(page,name,item){await click(page,name);await click(page,item);}
async function canvasPixel(page,x,y){
  const canvas=await waitForControl(page,'Image canvas'), view=await state(page);
  await page.mouse.move(10,10);
  return decodePng(await page.screenshot()).pixel(canvas.x+view.panX+x*view.zoom,canvas.y+view.panY+y*view.zoom).slice(0,3);
}
async function previewPixel(page,x,y){
  const canvas=await waitForControl(page,'Filter Gallery preview');
  const scale=Math.min(canvas.width/512,canvas.height/256);
  const left=canvas.x+(canvas.width-512*scale)/2, top=canvas.y+(canvas.height-256*scale)/2;
  await page.mouse.move(10,10);
  return decodePng(await page.screenshot()).pixel(left+x*scale,top+y*scale).slice(0,3);
}

test('cooperative browser Gaussian fallback blurs real pixels and keeps preview/cancel outside history',async({page})=>{
  const errors=[];
  page.on('pageerror',error=>errors.push(error.message));
  await page.addInitScript(()=>Object.defineProperty(navigator,'gpu',{value:undefined,configurable:true}));
  await page.goto('./?test=1',{waitUntil:'domcontentloaded'});
  await waitForWorkspace(page);
  // Independent opaque edge PNG, imported through the real file picker. This
  // creates test input only and does not invoke a C# document mutation endpoint.
  const png=await page.evaluate(()=>{
    const canvas=document.createElement('canvas');canvas.width=512;canvas.height=256;
    const context=canvas.getContext('2d');
    context.fillStyle='#000000';context.fillRect(0,0,256,256);
    context.fillStyle='#ffffff';context.fillRect(256,0,256,256);
    return canvas.toDataURL('image/png').split(',')[1];
  });
  await click(page,'File');
  const choosing=page.waitForEvent('filechooser');await click(page,'Open…');
  await(await choosing).setFiles({name:'Gaussian-edge.png',mimeType:'image/png',buffer:Buffer.from(png,'base64')});
  await expect.poll(async()=>(await state(page)).width).toBe(512);
  await expect.poll(async()=>(await state(page)).height).toBe(256);
  const before=await state(page);
  const original=await canvasPixel(page,250,128);
  expect(original).toEqual([0,0,0]);
  await menu(page,'Filter','Filter Gallery…');
  await waitForControl(page,'Filter Gallery preview ready');
  await click(page,'Add GaussianBlur');
  await waitForControl(page,'Effect 2: GaussianBlur');
  await click(page,'Gallery amount');await page.keyboard.press('Control+a');
  await page.keyboard.insertText('16');await page.keyboard.press('Enter');
  await expect.poll(async()=>(await previewPixel(page,250,128))[0],{timeout:30000}).toBeGreaterThan(40);
  const preview=await previewPixel(page,250,128);
  expect(Math.max(...preview)-Math.min(...preview)).toBeLessThanOrEqual(1);
  expect(preview[0]).toBeLessThan(140);
  expect((await state(page)).history).toBe(before.history);
  expect((await state(page)).contentRevision).toBe(before.contentRevision);
  expect(await page.evaluate(()=>imageSpaceGpu.describe().available)).toBe(false);
  await click(page,'Apply filters');
  await expect.poll(async()=>(await state(page)).history,{timeout:30000}).toBe(before.history+1);
  await expect.poll(async()=>(await state(page)).busy).toBe(false);
  const applied=await canvasPixel(page,250,128);
  expect(applied[0]).toBeGreaterThan(40);expect(applied[0]).toBeLessThan(140);
  expect(Math.max(...applied)-Math.min(...applied)).toBeLessThanOrEqual(1);
  await click(page,'Move tool (V)');await page.keyboard.press('Control+z');
  await expect.poll(()=>canvasPixel(page,250,128)).toEqual(original);
  await page.keyboard.press('Control+Shift+z');
  await expect.poll(()=>canvasPixel(page,250,128)).toEqual(applied);
  const committed=await state(page);
  await menu(page,'Filter','Filter Gallery…');
  await waitForControl(page,'Filter Gallery preview ready');
  await click(page,'Cancel');
  await expect.poll(async()=>(await state(page)).dialog).toBe(false);
  expect((await state(page)).history).toBe(committed.history);
  expect((await state(page)).contentRevision).toBe(committed.contentRevision);
  await expect.poll(()=>canvasPixel(page,250,128)).toEqual(applied);
  expect(await page.evaluate(()=>imageSpaceGpu.describe().liveSessions)).toBe(0);
  expect(errors).toEqual([]);
  await mkdir('artifacts/screenshots',{recursive:true});
  await page.screenshot({path:'artifacts/screenshots/streaming-gaussian-fallback.png'});
});
