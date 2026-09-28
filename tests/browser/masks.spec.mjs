import { test, expect } from '@playwright/test';
import { mkdir, readFile } from 'node:fs/promises';
import { waitForWorkspace } from './readiness.mjs';
import { decodePng } from './png.mjs';

const state = page => page.evaluate(() => globalThis.imageSpaceDiagnostics);
async function control(page, name) {
  await expect.poll(() => page.evaluate(value => globalThis.imageSpaceControls?.some(c => c.name === value && c.enabled), name)).toBe(true);
  return page.evaluate(value => globalThis.imageSpaceControls.find(c => c.name === value && c.enabled), name);
}
async function click(page, name) {
  const c = await control(page, name);
  await page.mouse.click(c.x + c.width / 2, c.y + c.height / 2);
}
async function point(page, x, y) {
  const c = await control(page, 'Image canvas'), s = await state(page);
  return { x: c.x + s.panX + x * s.zoom, y: c.y + s.panY + y * s.zoom };
}
async function pixel(page, p) { return decodePng(await page.screenshot()).pixel(p.x, p.y).slice(0, 3); }
const distance = (a, b) => a.reduce((sum, channel, i) => sum + Math.abs(channel - b[i]), 0);
async function drag(page, from, to) {
  await page.mouse.move(from.x, from.y); await page.mouse.down();
  await page.mouse.move(to.x, to.y, { steps: 12 }); await page.mouse.up();
}
async function number(page, name, value) {
  await click(page, name); await page.keyboard.press('Control+a');
  await page.keyboard.insertText(String(value)); await page.keyboard.press('Enter');
}
async function boot(page) {
  await page.goto('./?test=1', { waitUntil: 'domcontentloaded' });
  await waitForWorkspace(page);
}
async function invertAdjustment(page) {
  await click(page, 'New adjustment layer'); await click(page, 'Invert');
  await expect.poll(async () => (await state(page)).activeAdjustment).toBe('Invert');
}
async function capture(page, name) {
  await mkdir('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: `artifacts/screenshots/${name}.png` });
}

test('adjustment masks restrict the effect and support real brush, undo and inspection input', async ({ page }) => {
  await boot(page);
  const outside = await point(page, 300, 100), inside = await point(page, 800, 100);
  const originalOutside = await pixel(page, outside), originalInside = await pixel(page, inside);
  await click(page, 'Marquee tool (M)');
  await drag(page, await point(page, 600, 70), await point(page, 920, 180));
  await expect.poll(async () => (await state(page)).selection).toBe(true);
  await invertAdjustment(page);
  await click(page, 'Add layer mask');
  await expect.poll(async () => (await state(page)).editMask).toBe(true);
  await expect.poll(async () => distance(await pixel(page, outside), originalOutside)).toBeLessThanOrEqual(3);
  await expect.poll(async () => distance(await pixel(page, inside), originalInside)).toBeGreaterThan(40);
  await page.keyboard.press('Control+d');
  await expect.poll(async () => (await state(page)).selection).toBe(false);
  await click(page, 'Brush tool (B)'); await page.keyboard.press('d');
  const history = (await state(page)).history;
  const edited = await point(page, 740, 120), baseline = await pixel(page, edited);
  await drag(page, await point(page, 690, 120), await point(page, 790, 120));
  await page.mouse.move(10, 10);
  await expect.poll(async () => (await state(page)).history).toBe(history + 1);
  await expect.poll(async () => distance(await pixel(page, edited), baseline)).toBeGreaterThan(20);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).history).toBe(history);
  await expect.poll(() => pixel(page, edited)).toEqual(baseline);
  await page.keyboard.press('Control+Shift+z');
  await expect.poll(async () => (await state(page)).history).toBe(history + 1);
  await click(page, 'Mask view Grayscale');
  await expect.poll(async () => (await state(page)).maskPreview).toBe('Grayscale');
  await expect.poll(async () => (await pixel(page, outside))[0]).toBeLessThan(3);
  const viewHistory = (await state(page)).history;
  await click(page, 'Mask view Overlay');
  await expect.poll(async () => (await state(page)).maskPreview).toBe('Overlay');
  expect((await state(page)).history).toBe(viewHistory);
  await capture(page, 'adjustment-mask-overlay');
  await click(page, 'Mask view Composite');
  await click(page, 'Back to layer content');
  await expect.poll(async () => (await state(page)).editMask).toBe(false);
  await expect.poll(async () => (await state(page)).maskPreview).toBe('Composite');
});

test('mask properties survive undo, lock protection and native archive roundtrip', async ({ page }) => {
  await boot(page); await invertAdjustment(page); await click(page, 'Add layer mask');
  await expect.poll(async () => (await state(page)).editMask).toBe(true);
  await number(page, 'Mask density (%)', 35);
  await expect.poll(async () => (await state(page)).maskDensity).toBeCloseTo(.35, 5);
  await number(page, 'Mask feather (px)', 6);
  await expect.poll(async () => (await state(page)).maskFeather).toBe(6);
  // Leave text input before shortcuts; typing must retain ordinary textbox key routing.
  await click(page, 'Mask view Composite');
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).maskFeather).toBe(0);
  await page.keyboard.press('Control+Shift+z');
  await expect.poll(async () => (await state(page)).maskFeather).toBe(6);
  await click(page, 'Invert mask'); await click(page, 'Toggle mask');
  await expect.poll(async () => (await state(page)).maskEnabled).toBe(false);
  await click(page, 'Lock layer');
  await expect.poll(async () => (await state(page)).activeLocked).toBe(true);
  await expect.poll(() => page.evaluate(() => globalThis.imageSpaceControls?.find(c => c.name === 'Invert mask')?.enabled)).toBe(false);
  await click(page, 'Unlock layer');
  await expect.poll(async () => (await state(page)).activeLocked).toBe(false);
  await click(page, 'Mask view Grayscale');
  await capture(page, 'mask-density-feather');
  const downloadPromise = page.waitForEvent('download');
  await page.keyboard.press('Control+s');
  const download = await downloadPromise, path = await download.path();
  const chooserPromise = page.waitForEvent('filechooser');
  await page.keyboard.press('Control+o');
  await (await chooserPromise).setFiles({ name: 'Masked-adjustment.imagespace', mimeType: 'application/x-imagespace', buffer: await readFile(path) });
  await expect.poll(async () => (await state(page)).documents, { timeout: 30000 }).toBe(2);
  const restored = await state(page);
  expect(restored.activeAdjustment).toBe('Invert'); expect(restored.mask).toBe(true);
  expect(restored.maskDensity).toBeCloseTo(.35, 5); expect(restored.maskFeather).toBe(6); expect(restored.maskEnabled).toBe(false);
  await click(page, 'Edit mask for Invert');
  await expect.poll(async () => (await state(page)).editMask).toBe(true);
  await click(page, 'Toggle mask'); await click(page, 'Mask view Grayscale');
  await expect.poll(async () => (await state(page)).maskEnabled).toBe(true);
  // Density .35 with a fully hidden authored mask produces effective coverage .65.
  const sample = await point(page, 500, 300);
  await expect.poll(async () => (await pixel(page, sample))[0]).toBeGreaterThanOrEqual(164);
  await expect.poll(async () => (await pixel(page, sample))[0]).toBeLessThanOrEqual(167);
});
