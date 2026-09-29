import {test,expect} from '@playwright/test';
import {mkdir,readFile} from 'node:fs/promises';
import {waitForWorkspace} from './readiness.mjs';
import {decodePng} from './png.mjs';
const state=page=>page.evaluate(()=>globalThis.imageSpaceDiagnostics);
async function control(page,name){
  await expect.poll(()=>page.evaluate(n=>globalThis.imageSpaceControls?.some(c=>c.name===n&&c.enabled),name),{message:`Visible control: ${name}`}).toBe(true);
  return page.evaluate(n=>globalThis.imageSpaceControls.find(c=>c.name===n&&c.enabled),name);
}
async function click(page,name){const c=await control(page,name);await page.mouse.click(c.x+c.width/2,c.y+c.height/2);}
async function menu(page,name,command){await click(page,name);await click(page,command);}
async function ready(page){
  await page.goto('./?test=1',{waitUntil:'domcontentloaded'});await waitForWorkspace(page);
  await click(page,'New pixel layer');await expect.poll(async()=>(await state(page)).layers).toBe(9);
  await menu(page,'Edit','Fill with foreground');await expect.poll(async()=>(await state(page)).history).toBe(2);
}
async function rgb(page,name){
  const c=await control(page,name);await page.mouse.move(10,10);
  return decodePng(await page.screenshot()).pixel(c.x+c.width/2,c.y+c.height/2).slice(0,3);
}
async function gallery(page){
  await menu(page,'Filter','Filter Gallery…');await expect.poll(async()=>(await state(page)).dialog).toBe(true);
  await control(page,'Filter Gallery preview ready');
}
async function noSessions(page){await expect.poll(()=>page.evaluate(()=>globalThis.imageSpaceGpu.describe().liveSessions)).toBe(0);}

test('gallery previews stay outside history; ordered stack applies once and roundtrips',async({page})=>{
  const errors=[];page.on('pageerror',error=>errors.push(error.message));
  await ready(page);const original=await rgb(page,'Image canvas');expect(original).toEqual([54,145,230]);
  const before=await state(page);await gallery(page);
  await expect.poll(()=>rgb(page,'Filter Gallery preview')).toEqual([132,132,132]);
  await click(page,'Add Invert');
  await expect.poll(()=>rgb(page,'Filter Gallery preview')).toEqual([123,123,123]);
  expect((await state(page)).history).toBe(before.history);
  expect((await state(page)).contentRevision).toBe(before.contentRevision);
  await mkdir('artifacts/screenshots',{recursive:true});
  await page.screenshot({path:'artifacts/screenshots/filter-gallery.png'});
  await click(page,'Apply filters');
  await expect.poll(async()=>(await state(page)).history).toBe(before.history+1);
  await expect.poll(async()=>(await state(page)).busy).toBe(false);
  await expect.poll(()=>rgb(page,'Image canvas')).toEqual([123,123,123]);await noSessions(page);
  await click(page,'Move tool (V)');await page.keyboard.press('Control+z');
  await expect.poll(()=>rgb(page,'Image canvas')).toEqual(original);
  await page.keyboard.press('Control+Shift+z');await expect.poll(()=>rgb(page,'Image canvas')).toEqual([123,123,123]);
  const downloading=page.waitForEvent('download');await page.keyboard.press('Control+s');const path=await(await downloading).path();
  const choosing=page.waitForEvent('filechooser');await page.keyboard.press('Control+o');
  await(await choosing).setFiles({name:'Filter-gallery.imagespace',mimeType:'application/x-imagespace',buffer:await readFile(path)});
  await expect.poll(async()=>(await state(page)).documents).toBe(2);
  await expect.poll(()=>rgb(page,'Image canvas')).toEqual([123,123,123]);
  expect(errors).toEqual([]);
});

test('gallery cancel, bypass, reorder, remove and repeat use immutable captured settings',async({page})=>{
  await ready(page);const before=await state(page);await gallery(page);
  await click(page,'Add Invert');await expect.poll(()=>rgb(page,'Filter Gallery preview')).toEqual([123,123,123]);
  await click(page,'Enable gallery effect 2');await expect.poll(()=>rgb(page,'Filter Gallery preview')).toEqual([132,132,132]);
  await click(page,'Enable gallery effect 2');await click(page,'Move effect up');
  await control(page,'Effect 1: Invert');await control(page,'Effect 2: Grayscale');
  await click(page,'Remove effect');await control(page,'Effect 1: Grayscale');
  await expect.poll(()=>rgb(page,'Filter Gallery preview')).toEqual([132,132,132]);
  await click(page,'Cancel');await expect.poll(async()=>(await state(page)).dialog).toBe(false);await noSessions(page);
  expect((await state(page)).history).toBe(before.history);
  await expect.poll(()=>rgb(page,'Image canvas')).toEqual([54,145,230]);
  await gallery(page);await click(page,'Apply filters');await expect.poll(async()=>(await state(page)).history).toBe(before.history+1);
  await expect.poll(()=>rgb(page,'Image canvas')).toEqual([132,132,132]);
  await menu(page,'Filter','Repeat filter stack');await expect.poll(async()=>(await state(page)).history).toBe(before.history+2);
  await expect.poll(()=>rgb(page,'Image canvas')).toEqual([132,132,132]);await noSessions(page);
});

test('gallery CPU fallback without WebGPU remains functional and leaves no preview edits',async({page})=>{
  await page.addInitScript(()=>Object.defineProperty(navigator,'gpu',{value:undefined,configurable:true}));
  await ready(page);const before=await state(page);await gallery(page);
  await expect.poll(()=>rgb(page,'Filter Gallery preview')).toEqual([132,132,132]);
  expect(await page.evaluate(()=>imageSpaceGpu.describe().available)).toBe(false);
  await click(page,'Add Gamma');await click(page,'Gallery amount');await page.keyboard.press('Control+a');
  await page.keyboard.insertText('1');await page.keyboard.press('Enter');
  await click(page,'Apply filters');await expect.poll(async()=>(await state(page)).history).toBe(before.history+1);
  await expect.poll(()=>rgb(page,'Image canvas')).toEqual([132,132,132]);
});
