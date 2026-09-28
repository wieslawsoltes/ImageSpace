import {test, expect} from '@playwright/test';
import {mkdir, readFile} from 'node:fs/promises';
import {waitForWorkspace} from './readiness.mjs';

const state = page => page.evaluate(() => globalThis.imageSpaceDiagnostics);
async function control(page, name) {
  await expect.poll(() => page.evaluate(n => globalThis.imageSpaceControls?.some(c => c.name === n && c.enabled), name)).toBe(true);
  return page.evaluate(n => globalThis.imageSpaceControls.find(c => c.name === n && c.enabled), name);
}
async function click(page, name) {const c = await control(page, name); await page.mouse.click(c.x+c.width/2,c.y+c.height/2);}
async function point(page,x,y) {const c=await control(page,'Image canvas'),s=await state(page);return{x:c.x+s.panX+x*s.zoom,y:c.y+s.panY+y*s.zoom};}
async function boot(page) {await page.goto('./?test=1',{waitUntil:'domcontentloaded'});await waitForWorkspace(page);}

test('selection modifiers execute through custom Uno menus and support undo',async({page})=>{
  await boot(page); await click(page,'Marquee tool (M)');
  const a=await point(page,500,300),b=await point(page,700,450);
  await page.mouse.move(a.x,a.y);await page.mouse.down();await page.mouse.move(b.x,b.y,{steps:8});await page.mouse.up();
  await expect.poll(async()=>(await state(page)).selection).toBe(true);
  for(const operation of ['Expand','Contract','Border','Smooth']) {
    const history=(await state(page)).history;
    await click(page,'Select');await click(page,operation+' selection…');
    await expect.poll(async()=>(await state(page)).dialog).toBe(true);
    await click(page,'Radius');await page.keyboard.press('Control+a');await page.keyboard.insertText('5');await page.keyboard.press('Enter');
    await click(page,'Apply');
    await expect.poll(async()=>(await state(page)).history).toBe(history+1);
    expect((await state(page)).status).toContain(operation+' applied');
    await click(page,'Marquee tool (M)');await page.keyboard.press('Control+z');
    await expect.poll(async()=>(await state(page)).history).toBe(history);
  }
  await mkdir('artifacts/screenshots',{recursive:true});await page.screenshot({path:'artifacts/screenshots/selection-modifiers.png'});
});

test('ZIP-predicted PSD with Unicode name and user mask imports through browser file picker',async({page})=>{
  await boot(page);
  const chooser=page.waitForEvent('filechooser');await click(page,'File');await click(page,'Open…');
  await (await chooser).setFiles({name:'Layer-mask-zip-prediction.psd',mimeType:'image/vnd.adobe.photoshop',buffer:await readFile('tests/browser/fixtures/user-mask-zip.psd')});
  await expect.poll(async()=>(await state(page)).documents,{timeout:30000}).toBe(2);
  await expect.poll(async()=>(await state(page)).dialog).toBe(true);await click(page,'Done');
  const current=await state(page);
  expect(current.layers).toBe(1);expect(current.width).toBe(32);expect(current.activeLayer).toBe('Mask Ω 🎨');
  expect(current.mask).toBe(true);expect(current.maskDensity).toBeCloseTo(128/255,5);expect(current.maskFeather).toBe(2);
  await click(page,'Edit mask for Mask Ω 🎨');await expect.poll(async()=>(await state(page)).editMask).toBe(true);
  await click(page,'Mask view Grayscale');
  await mkdir('artifacts/screenshots',{recursive:true});await page.screenshot({path:'artifacts/screenshots/psd-user-mask.png'});
});
