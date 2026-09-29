import {test, expect} from '@playwright/test';
import {waitForWorkspace} from './readiness.mjs';
import {waitForControl as control} from './control.mjs';
import {nativeFixture} from './native-fixture.mjs';
import {decodePng} from './png.mjs';

const state = page => page.evaluate(() => globalThis.imageSpaceDiagnostics);
async function click(page, name) {
  const c = await control(page, name);
  await page.mouse.click(c.x + c.width / 2, c.y + c.height / 2);
}

test('inserting an adjustment above a clipping base preserves the chain and undo target', async ({page}) => {
  await page.goto('./?test=1', {waitUntil:'domcontentloaded'});
  await waitForWorkspace(page);
  const id = n => `d75ec628-d26e-44b9-9a73-${String(n).padStart(12, '0')}`;
  const bytes = nativeFixture({version:5, id:id(0), name:'Insertion fixture', width:400, height:240, activeLayerId:id(2),
    layers:[
      {metadata:{id:id(1), name:'Backdrop', kind:2, width:400, height:240, color:{r:48,g:64,b:80,a:255}}},
      {metadata:{id:id(2), name:'Base', kind:2, x:100, y:60, width:200, height:120, color:{r:255,g:255,b:255,a:255}}},
      {metadata:{id:id(3), name:'Clipped color', kind:2, width:400, height:240, isClipped:true, color:{r:255,g:0,b:0,a:255}}}
    ]});
  await click(page, 'File');
  const chooser = page.waitForEvent('filechooser');
  await click(page, 'Open…');
  await (await chooser).setFiles({name:'Insertion.imagespace', mimeType:'application/x-imagespace', buffer:bytes});
  await expect.poll(async () => (await state(page)).activeLayer).toBe('Base');
  await click(page, 'New adjustment layer'); await click(page, 'Invert');
  await expect.poll(async () => (await state(page)).layers).toBe(4);
  await expect.poll(async () => (await state(page)).activeAdjustment).toBe('Invert');
  const inserted = await state(page);
  expect(inserted.activeClipped).toBe(true);
  expect(inserted.clippingBase).toBe('Base');
  expect(inserted.history).toBe(1);
  const canvas = await control(page, 'Image canvas');
  await page.mouse.move(10, 10);
  await expect.poll(async () => {
    const image = decodePng(await page.screenshot());
    return image.pixel(canvas.x + inserted.panX + 40 * inserted.zoom, canvas.y + inserted.panY + 120 * inserted.zoom).slice(0, 3);
  }).toEqual([48,64,80]);
  await click(page, 'Move tool (V)'); await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).layers).toBe(3);
  expect((await state(page)).activeLayer).toBe('Base');
  await page.keyboard.press('Control+Shift+z');
  await expect.poll(async () => (await state(page)).layers).toBe(4);
  expect((await state(page)).activeClipped).toBe(true);
});
