import { test, expect } from '@playwright/test';
import { mkdir, readFile } from 'node:fs/promises';
import { waitForWorkspace } from './readiness.mjs';
import { waitForControl as control } from './control.mjs';
import { decodePng } from './png.mjs';
import { nativeFixture } from './native-fixture.mjs';
const state = page => page.evaluate(() => globalThis.imageSpaceDiagnostics);
async function click(page, name) { const c = await control(page, name); await page.mouse.click(c.x + c.width / 2, c.y + c.height / 2); }
async function menu(page, name, command) { await click(page, name); await click(page, command); }
async function point(page, x, y) {
  const c = await control(page, 'Image canvas'), s = await state(page);
  return { x: c.x + s.panX + x * s.zoom, y: c.y + s.panY + y * s.zoom };
}
async function pixel(page, p) { return decodePng(await page.screenshot()).pixel(p.x, p.y).slice(0, 3); }
async function screenshot(page, name) {
  await mkdir('artifacts/screenshots', { recursive: true }); await page.screenshot({ path: `artifacts/screenshots/${name}.png` });
}
const id = n => `83721214-7e17-43d3-a495-${String(n).padStart(12, '0')}`;
function fixture(clipped = false, alpha = 255, opacity = 1) {
  return nativeFixture({ version: clipped ? 5 : 1, id: id(0), name: 'Clipping fixture', width: 400, height: 240, activeLayerId: id(3),
    layers: [
      { metadata: { id: id(1), name: 'Backdrop', kind: 2, width: 400, height: 240, color: { r: 48, g: 64, b: 80, a: 255 } } },
      { metadata: { id: id(2), name: 'Base', kind: 2, x: 100, y: 60, width: 200, height: 120, opacity, color: { r: 255, g: 255, b: 255, a: alpha } } },
      { metadata: { id: id(3), name: 'Clipped color', kind: 2, width: 400, height: 240, isClipped: clipped, color: { r: 255, g: 0, b: 0, a: 255 } } }
    ] });
}
async function boot(page, bytes = fixture()) {
  await page.goto('./?test=1', { waitUntil: 'domcontentloaded' }); await waitForWorkspace(page);
  await click(page, 'File'); const choosing = page.waitForEvent('filechooser'); await click(page, 'Open…');
  await (await choosing).setFiles({ name: 'Clipping.imagespace', mimeType: 'application/x-imagespace', buffer: bytes });
  await expect.poll(async () => (await state(page)).name).toBe('Clipping fixture');
  await expect.poll(async () => (await state(page)).layers).toBe(3);
  await page.mouse.move(10, 10);
}

test('clipping mask commands preserve outside pixels, undo, and editable native relationships', async ({ page }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message)); await boot(page);
  const inside = await point(page, 200, 120), outside = await point(page, 40, 120);
  await expect.poll(() => pixel(page, outside)).toEqual([255, 0, 0]);
  const before = await state(page); await menu(page, 'Layer', 'Create Clipping Mask');
  await expect.poll(async () => (await state(page)).activeClipped).toBe(true);
  await expect.poll(async () => (await state(page)).history).toBe(before.history + 1);
  await page.mouse.move(10, 10);
  await expect.poll(() => pixel(page, outside)).toEqual([48, 64, 80]);
  await expect.poll(() => pixel(page, inside)).toEqual([255, 0, 0]);
  expect((await state(page)).clippingBase).toBe('Base');
  await screenshot(page, 'clipping-layer-controls');
  await click(page, 'Move tool (V)'); await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).activeClipped).toBe(false);
  await expect.poll(() => pixel(page, outside)).toEqual([255, 0, 0]);
  await page.keyboard.press('Control+Shift+z'); await expect.poll(async () => (await state(page)).activeClipped).toBe(true);
  const downloading = page.waitForEvent('download'); await page.keyboard.press('Control+s'); const path = await (await downloading).path();
  const choosing = page.waitForEvent('filechooser'); await page.keyboard.press('Control+o');
  await (await choosing).setFiles({ name: 'Saved-clipping.imagespace', mimeType: 'application/x-imagespace', buffer: await readFile(path) });
  await expect.poll(async () => (await state(page)).documents).toBe(3);
  expect((await state(page)).activeClipped).toBe(true);
  await expect.poll(() => pixel(page, outside)).toEqual([48, 64, 80]);
  await click(page, 'Move tool (V)'); await page.keyboard.press('Control+Alt+g');
  await expect.poll(async () => (await state(page)).activeClipped).toBe(false);
  await expect.poll(() => pixel(page, outside)).toEqual([255, 0, 0]);
  expect(errors).toEqual([]);
});

test('clipped preview uses base opacity exactly once and rejects hit targets outside its base', async ({ page }) => {
  await boot(page, fixture(true, 128, .5));
  const inside = await point(page, 200, 120), outside = await point(page, 40, 120);
  await expect.poll(async () => {
    const value = await pixel(page, inside), expected = [100, 48, 60];
    return Math.max(...value.map((c, i) => Math.abs(c - expected[i])));
  }).toBeLessThanOrEqual(2);
  await expect.poll(() => pixel(page, outside)).toEqual([48, 64, 80]);
  await click(page, 'Move tool (V)'); const before = await state(page);
  await page.mouse.click(outside.x, outside.y);
  await expect.poll(async () => (await state(page)).activeLayer).toBe('Backdrop');
  await page.mouse.click(inside.x, inside.y);
  await expect.poll(async () => (await state(page)).activeLayer).toBe('Clipped color');
  const after = await state(page);
  expect(after.history).toBe(before.history); expect(after.contentRevision).toBe(before.contentRevision);
  expect(after.inspectorBuilds).toBe(before.inspectorBuilds); expect(after.sceneRenders).toBe(before.sceneRenders);
});

test('editable clipping study renders GPU-capable non-normal blends and clipped adjustments', async ({ page }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await page.goto('./?test=1', { waitUntil: 'domcontentloaded' }); await waitForWorkspace(page);
  await menu(page, 'File', 'Clipping mask study');
  await expect.poll(async () => (await state(page)).name).toBe('Clipping study');
  await expect.poll(async () => (await state(page)).clippingBlenderBuilds).toBeGreaterThan(0);
  const center = await point(page, 650, 300), corner = await point(page, 120, 126);
  await expect.poll(() => pixel(page, corner)).toEqual([25, 29, 38]);
  expect(await pixel(page, center)).not.toEqual([25, 29, 38]);
  await page.mouse.move(10, 10); await screenshot(page, 'clipping-study');
  const before = await state(page);
  await click(page, 'Clipped saturation'); await click(page, 'Aurora texture');
  const after = await state(page);
  expect(after.clippingBlenderBuilds).toBe(before.clippingBlenderBuilds);
  expect(after.tileUploads).toBe(before.tileUploads); expect(after.history).toBe(0);
  expect(errors).toEqual([]);
});
