import {test,expect} from '@playwright/test';
import {mkdir,writeFile} from 'node:fs/promises';
import {decodePng,colorCount} from './png.mjs';
import {waitForWorkspace} from './readiness.mjs';

async function boot(page){
  const errors=[];page.on('pageerror',e=>errors.push(e.message));await page.goto('./?test=1',{waitUntil:'domcontentloaded'});
  await waitForWorkspace(page);return errors;
}
async function state(page){return page.evaluate(()=>globalThis.imageSpaceDiagnostics);}
async function control(page,name){await expect.poll(()=>page.evaluate(n=>globalThis.imageSpaceControls?.filter(c=>c.name===n&&c.enabled).length||0,name),{message:`Control '${name}' must be visible`}).toBeGreaterThan(0);return page.evaluate(n=>globalThis.imageSpaceControls.find(c=>c.name===n&&c.enabled),name);}
async function click(page,name){const c=await control(page,name);await page.mouse.click(c.x+c.width/2,c.y+c.height/2);await page.waitForTimeout(180);}
async function menu(page,name,item){await click(page,name);await click(page,item);}
async function world(page,x,y){const c=await control(page,'Image canvas'),s=await state(page);return {x:c.x+s.panX+x*s.zoom,y:c.y+s.panY+y*s.zoom};}
async function drag(page,a,b){await page.mouse.move(a.x,a.y);await page.mouse.down();await page.mouse.move(b.x,b.y,{steps:12});await page.mouse.up();await page.waitForTimeout(400);}
async function screenshot(page,name){await mkdir('artifacts/screenshots',{recursive:true});return page.screenshot({path:`artifacts/screenshots/${name}.png`});}
async function pixel(page,point){return decodePng(await page.screenshot()).pixel(point.x,point.y).slice(0,3);}
const distance=(a,b)=>a.reduce((sum,value,index)=>sum+Math.abs(value-b[index]),0);

test('real Uno workspace renders original artwork and custom controls',async({page})=>{
  const errors=await boot(page);expect((await state(page)).layers).toBe(8);expect(await page.locator('canvas').count()).toBeGreaterThan(0);
  const identityResponse=await page.request.get('./build-info.json');
  expect(identityResponse.ok()).toBe(true);
  const identity=await identityResponse.json();
  expect((await state(page)).version).toBe(identity.version);
  for(const name of ['File','Edit','Layer','Filter','Brush tool (B)','New pixel layer','Image canvas'])await control(page,name);
  const image=decodePng(await screenshot(page,'workspace'));const c=await control(page,'Image canvas');expect(colorCount(image,c)).toBeGreaterThan(1000);
  const p=await world(page,720,245);const sun=image.pixel(p.x,p.y);expect(sun[0]).toBeGreaterThan(180);expect(sun[0]).toBeGreaterThan(sun[2]);
  await click(page,'History');await screenshot(page,'history');await click(page,'Layers');
  await menu(page,'Help','Features and compatibility');await expect.poll(async()=> (await state(page)).dialog).toBe(true);await screenshot(page,'compatibility');await click(page,'Done');expect(errors).toEqual([]);
});

test('paint and undo operate through real pointer and keyboard events',async({page})=>{
  await boot(page);await click(page,'New pixel layer');await expect.poll(async()=> (await state(page)).layers).toBe(9);await click(page,'Brush tool (B)');
  const a=await world(page,560,460),b=await world(page,810,450);await page.mouse.move(12,10);const mid=await world(page,680,455);const old=await pixel(page,mid);
  await drag(page,a,b);await page.mouse.move(12,10);await expect.poll(async()=> (await state(page)).history).toBe(2);
  // Committed model state can precede presentation by a compositor frame on cold CDN starts.
  // Keep a real pixel assertion and wait for presentation, rather than extending a fixed sleep.
  await expect.poll(async()=>distance(await pixel(page,mid),old),{message:'The painted stroke must change visible pixels.'}).toBeGreaterThan(20);
  const painted=decodePng(await screenshot(page,'painted'));expect(distance(painted.pixel(mid.x,mid.y).slice(0,3),old)).toBeGreaterThan(20);
  await page.keyboard.press('Control+z');await expect.poll(async()=> (await state(page)).history).toBe(1);await expect.poll(()=>pixel(page,mid),{message:'Undo must restore the exact pre-stroke RGB pixels.'}).toEqual(old);
  await page.keyboard.press('Control+Shift+z');await expect.poll(async()=> (await state(page)).history).toBe(2);await click(page,'Add layer mask');await expect.poll(async()=> (await state(page)).mask).toBe(true);expect((await state(page)).editMask).toBe(true);
  await menu(page,'Image','Invert');await expect.poll(async()=> (await state(page)).history).toBe(4);await screenshot(page,'mask-inverted');
});

test('shape transform, selection, crop and native document roundtrip',async({page})=>{
  await boot(page);await click(page,'Rectangle tool (U)');await drag(page,await world(page,520,360),await world(page,740,450));await expect.poll(async()=> (await state(page)).activeKind).toBe('Rectangle');
  await click(page,'Move tool (V)');const x=(await state(page)).activeX;await page.keyboard.press('Shift+ArrowRight');await expect.poll(async()=> (await state(page)).activeX).toBe(x+10);
  await click(page,'Marquee tool (M)');await drag(page,await world(page,500,330),await world(page,800,510));await expect.poll(async()=> (await state(page)).selection).toBe(true);await screenshot(page,'selection');await page.keyboard.press('Control+d');await expect.poll(async()=> (await state(page)).selection).toBe(false);
  await click(page,'Crop tool (C)');await drag(page,await world(page,100,100),await world(page,900,600));await page.keyboard.press('Enter');await expect.poll(async()=> (await state(page)).width).toBeGreaterThanOrEqual(798);expect((await state(page)).width).toBeLessThanOrEqual(802);await page.keyboard.press('Control+z');await expect.poll(async()=> (await state(page)).width).toBe(1000);
  const downloadPromise=page.waitForEvent('download');await page.keyboard.press('Control+s');const download=await downloadPromise;expect(download.suggestedFilename()).toMatch(/\.imagespace$/);const file=await download.path();expect(file).toBeTruthy();await expect.poll(async()=> (await state(page)).dirty).toBe(false);
  const chooserPromise=page.waitForEvent('filechooser');await page.keyboard.press('Control+o');const chooser=await chooserPromise;await chooser.setFiles({name:'Roundtrip.imagespace',mimeType:'application/x-imagespace',buffer:await (await import('node:fs/promises')).readFile(file)});await expect.poll(async()=> (await state(page)).documents,{timeout:30000}).toBe(2);expect((await state(page)).layers).toBe(9);await screenshot(page,'roundtrip');
});

test('browser WebGPU kernels either validate on an adapter or explicitly use fallback',async({page})=>{
  await boot(page);const result=await page.evaluate(async()=>{
    if(!globalThis.imageSpaceGpu)throw new Error('The shipped WebGPU library is missing.');await imageSpaceGpu.initialize();const capabilities=imageSpaceGpu.describe();if(!capabilities.available)return {capabilities,checks:[]};
    const source=new Uint8Array([100,50,200,255,10,20,30,128,0,0,0,0,255,255,255,255]);const checks=[];
    for(const [kind,amount,secondary] of [['Invert',0,0],['Grayscale',0,0],['Sepia',0,0],['BrightnessContrast',10,20],['Saturation',-100,0],['Gamma',1,0],['Threshold',100,0],['Posterize',4,0]]){const output=await imageSpaceGpu.apply(source,2,2,kind,amount,secondary);checks.push({kind,bytes:Array.from(output)});}return {capabilities,checks};
  });
  await mkdir('artifacts',{recursive:true});await writeFile('artifacts/webgpu-results.json',JSON.stringify(result,null,2));
  if(!result.capabilities.available){expect(result.capabilities.backend).toMatch(/WebGPU unavailable|No WebGPU adapter/i);test.info().annotations.push({type:'gpu-fallback',description:result.capabilities.backend});return;}
  expect(result.checks).toHaveLength(8);const invert=result.checks.find(x=>x.kind==='Invert').bytes;expect(invert.slice(0,4)).toEqual([155,205,55,255]);expect(invert.slice(8,12)).toEqual([0,0,0,0]);expect(result.checks.find(x=>x.kind==='Gamma').bytes.slice(0,4)).toEqual([100,50,200,255]);for(const check of result.checks){expect(check.bytes[7]).toBe(128);expect(check.bytes[15]).toBe(255);}
});
