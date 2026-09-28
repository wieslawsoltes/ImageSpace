import { test, expect } from '@playwright/test';
import { mkdir, readFile } from 'node:fs/promises';
import { waitForWorkspace } from './readiness.mjs';
import { decodePng } from './png.mjs';

const state = page => page.evaluate(() => globalThis.imageSpaceDiagnostics);
async function control(page, name) {
  await expect.poll(() => page.evaluate(n => globalThis.imageSpaceControls?.some(c => c.name === n && c.enabled), name)).toBe(true);
  return page.evaluate(n => globalThis.imageSpaceControls.find(c => c.name === n && c.enabled), name);
}
async function click(page, name) {
  const c = await control(page, name);
  await page.mouse.click(c.x + c.width / 2, c.y + c.height / 2);
}
async function menu(page, name, command) { await click(page, name); await click(page, command); }
async function world(page, x, y) {
  const c = await control(page, 'Image canvas'), s = await state(page);
  return { x: c.x + s.panX + x * s.zoom, y: c.y + s.panY + y * s.zoom };
}
async function pixel(page, point) { return decodePng(await page.screenshot()).pixel(point.x, point.y).slice(0, 3); }
async function boot(page) {
  await page.goto('./?test=1', { waitUntil: 'domcontentloaded' });
  await waitForWorkspace(page);
}

test('apply layer mask retains visible pixels, records one edit, and survives native save and undo', async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await boot(page);
  await click(page, 'New pixel layer');
  await menu(page, 'Edit', 'Fill with foreground');
  await click(page, 'Marquee tool (M)');
  const from = await world(page, 520, 300), to = await world(page, 780, 480);
  await page.mouse.move(from.x, from.y); await page.mouse.down();
  await page.mouse.move(to.x, to.y, { steps: 10 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).selection).toBe(true);
  await click(page, 'Add layer mask');
  await expect.poll(async () => (await state(page)).mask).toBe(true);
  await page.keyboard.press('Control+d');
  await expect.poll(async () => (await state(page)).selection).toBe(false);
  await page.mouse.move(10, 10);
  const inside = await world(page, 650, 380), outside = await world(page, 870, 540);
  const beforeInside = await pixel(page, inside), beforeOutside = await pixel(page, outside);
  expect(beforeInside).not.toEqual(beforeOutside);
  const before = await state(page);
  await menu(page, 'Layer', 'Apply layer mask');
  await expect.poll(async () => (await state(page)).mask).toBe(false);
  await expect.poll(async () => (await state(page)).history).toBe(before.history + 1);
  expect((await state(page)).editMask).toBe(false);
  await page.mouse.move(10, 10);
  await expect.poll(() => pixel(page, inside)).toEqual(beforeInside);
  await expect.poll(() => pixel(page, outside)).toEqual(beforeOutside);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).mask).toBe(true);
  expect((await state(page)).editMask).toBe(true);
  await page.keyboard.press('Control+Shift+z');
  await expect.poll(async () => (await state(page)).mask).toBe(false);
  const downloading = page.waitForEvent('download');
  await page.keyboard.press('Control+s');
  const path = await (await downloading).path();
  const choosing = page.waitForEvent('filechooser');
  await page.keyboard.press('Control+o');
  await (await choosing).setFiles({ name: 'Applied-mask.imagespace', mimeType: 'application/x-imagespace', buffer: await readFile(path) });
  await expect.poll(async () => (await state(page)).documents).toBe(2);
  expect((await state(page)).mask).toBe(false);
  await page.mouse.move(10, 10);
  await expect.poll(() => pixel(page, inside)).toEqual(beforeInside);
  await expect.poll(() => pixel(page, outside)).toEqual(beforeOutside);
  await mkdir('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/applied-layer-mask.png' });
  expect(errors).toEqual([]);
});

test('applying a disabled mask is unavailable and its effect can be retained by enabling it', async ({ page }) => {
  await boot(page);
  await click(page, 'New pixel layer');
  await click(page, 'Add layer mask');
  await expect.poll(async () => (await state(page)).mask).toBe(true);
  await menu(page, 'Layer', 'Disable mask');
  await expect.poll(async () => (await state(page)).maskEnabled).toBe(false);
  await click(page, 'Layer');
  await expect.poll(() => page.evaluate(() => globalThis.imageSpaceControls?.some(c => c.name === 'Apply layer mask' && !c.enabled))).toBe(true);
  await page.keyboard.press('Escape');
  await menu(page, 'Layer', 'Enable mask');
  await expect.poll(async () => (await state(page)).maskEnabled).toBe(true);
  await menu(page, 'Layer', 'Apply layer mask');
  await expect.poll(async () => (await state(page)).mask).toBe(false);
});
