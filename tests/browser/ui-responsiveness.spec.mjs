import {test,expect} from '@playwright/test';
import {mkdir,writeFile} from 'node:fs/promises';
import {waitForWorkspace} from './readiness.mjs';
import {decodePng} from './png.mjs';

const state = page => page.evaluate(() => globalThis.imageSpaceDiagnostics);
async function control(page,name) {
  await expect.poll(() => page.evaluate(n => globalThis.imageSpaceControls?.some(c => c.name===n && c.enabled),name)).toBe(true);
  return page.evaluate(n => globalThis.imageSpaceControls.find(c => c.name===n && c.enabled),name);
}
async function click(page,name) { const c=await control(page,name); await page.mouse.click(c.x+c.width/2,c.y+c.height/2); }
async function menu(page,name,item) { await click(page,name); await click(page,item); }
async function choose(page,name) { await click(page,name); await expect.poll(async()=> (await state(page)).activeLayer).toBe(name); }
async function world(page,x,y) { const c=await control(page,'Image canvas'),s=await state(page);return {x:c.x+s.panX+x*s.zoom,y:c.y+s.panY+y*s.zoom}; }
async function number(page,name,value) { await click(page,name);await page.keyboard.press('Control+a');await page.keyboard.insertText(String(value));await page.keyboard.press('Enter'); }
async function boot(page) {
  await page.goto('./?test=1',{waitUntil:'domcontentloaded'});
  await waitForWorkspace(page);
  await expect.poll(async()=> (await state(page)).buttonTemplateBuilds).toBe(1);
}

const stable = ['buttonsCreated','buttonTemplateBuilds','layerRowsCreated','inspectorBuilds','optionsBuilds','tabsCreated','historyButtonsCreated'];

test('warm layer selection retains controls, creates no history and does not redraw image content',async({page})=>{
  await boot(page);
  const names=['After the light','Description','01  •  Evening sky','Footer rule'];
  for (const name of names) await choose(page,name);
  // Allow font/initial layout and read-only diagnostics to settle before sampling counters.
  await page.waitForTimeout(600);
  const before=await state(page), samples=[];
  for (let round=0;round<3;round++) for (const name of names) {
    await choose(page,name);
    const selected=await state(page);
    samples.push({target:name,uiRefreshMs:selected.lastSelectionRefreshMs});
  }
  const after=await state(page);
  for (const key of stable) expect(after[key],key).toBe(before[key]);
  expect(after.contentRevision).toBe(before.contentRevision);
  expect(after.history).toBe(before.history);
  expect(after.sceneRenders).toBe(before.sceneRenders);
  expect(after.toneHistogramBuilds).toBe(0);
  expect(after.channelHistogramBuilds).toBe(0);
  // A canvas click selects the sky but must not create an empty Move transaction.
  const p=await world(page,950,20);
  await page.mouse.click(p.x,p.y);
  await expect.poll(async()=> (await state(page)).activeLayer).toBe('01  •  Evening sky');
  expect((await state(page)).history).toBe(before.history);
  await mkdir('artifacts/browser-tests',{recursive:true});
  await writeFile('artifacts/browser-tests/ui-selection-performance.json',JSON.stringify({
    scope:'Synchronous C# inspector/workbench refresh after trusted pointer input. Not event-to-photon latency or GPU timing.',
    before,after,samples
  },null,2));
});

test('hover and animated selection overlays leave the retained image compositor untouched',async({page})=>{
  await boot(page);
  await click(page,'Marquee tool (M)');await page.keyboard.press('Control+a');
  await expect.poll(async()=> (await state(page)).selectionOutlineSegments).toBe(4);
  await page.waitForTimeout(600);
  const before=await state(page);
  for (let i=0;i<10;i++) { const p=await world(page,300+i*30,250);await page.mouse.move(p.x,p.y); }
  await page.waitForTimeout(900);
  const after=await state(page);
  expect(after.overlayRenders).toBeGreaterThan(before.overlayRenders);
  expect(after.sceneRenders).toBe(before.sceneRenders);
  expect(after.tileUploads).toBe(before.tileUploads);
  await page.keyboard.press('Control+d');
  await click(page,'New pixel layer');await click(page,'Brush tool (B)');
  const a=await world(page,560,460),b=await world(page,800,460);
  await page.waitForTimeout(300);const paintedBefore=await state(page);
  await page.mouse.move(a.x,a.y);await page.mouse.down();await page.mouse.move(b.x,b.y,{steps:10});await page.mouse.up();
  await expect.poll(async()=> (await state(page)).sceneRenders).toBeGreaterThan(paintedBefore.sceneRenders);
  await expect.poll(async()=> (await state(page)).history).toBe(paintedBefore.history+1);
});

test('reused inspectors update restored model objects and keep focused numeric edits',async({page})=>{
  await boot(page);
  await choose(page,'Footer rule');await number(page,'X',120);
  await expect.poll(async()=> (await state(page)).activeX).toBe(120);
  // Move focus out of the text editor before the global undo shortcut.
  await click(page,'Move tool (V)');await page.keyboard.press('Control+z');
  await expect.poll(async()=> (await state(page)).activeX).not.toBe(120);
  const restored=(await state(page)).activeX;
  await choose(page,'After the light');await choose(page,'Footer rule');
  const field=await control(page,'X');expect(Number(field.value)).toBe(restored);
  await number(page,'X',135);await expect.poll(async()=> (await state(page)).activeX).toBe(135);
  const before=await state(page);
  await click(page,'X');await page.keyboard.press('Control+a');await page.keyboard.insertText('142');
  await page.waitForTimeout(500); // diagnostics and layout must not replace uncommitted numeric text
  expect((await control(page,'X')).value).toBe('142');
  await page.keyboard.press('Enter');
  await expect.poll(async()=> (await state(page)).activeX).toBe(142);
  expect((await state(page)).inspectorBuilds).toBe(before.inspectorBuilds);
});

test('tone and channel histograms are deferred, bounded and reused when their image input is unchanged',async({page})=>{
  await boot(page);
  await click(page,'New adjustment layer');await click(page,'Levels');
  await expect.poll(async()=> (await state(page)).activeAdjustment).toBe('Levels');
  await expect.poll(async()=> (await state(page)).toneHistogramBuilds).toBe(1);
  const before=await state(page);
  await number(page,'Gamma',1.3);
  await expect.poll(async()=> (await state(page)).rgbGamma).toBeCloseTo(1.3,4);
  await click(page,'Levels channel Red');await page.waitForTimeout(600);
  expect((await state(page)).toneHistogramBuilds).toBe(before.toneHistogramBuilds);
  await click(page,'Levels channel RGB');
  await choose(page,'After the light');await choose(page,'Levels');await page.waitForTimeout(600);
  expect((await state(page)).inspectorBuilds).toBe(before.inspectorBuilds);
  expect((await state(page)).toneHistogramBuilds).toBe(before.toneHistogramBuilds);
  await click(page,'Channels');await expect.poll(async()=> (await state(page)).channelHistogramBuilds).toBe(1);
  await click(page,'Red histogram');await click(page,'Green histogram');await page.waitForTimeout(500);
  expect((await state(page)).channelHistogramBuilds).toBe(1);
  await click(page,'Layers');await choose(page,'Description');await page.waitForTimeout(500);
  expect((await state(page)).channelHistogramBuilds).toBe(1);
  await mkdir('artifacts/screenshots',{recursive:true});
  const shot=await page.screenshot({path:'artifacts/screenshots/retained-inspectors.png'});
  expect(decodePng(shot).width).toBe(1440);
});
