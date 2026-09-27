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
async function menu(page, name, item) { await click(page, name); await click(page, item); }
async function boot(page) {
  await page.goto('./?test=1', { waitUntil: 'domcontentloaded' });
  await waitForWorkspace(page);
}
async function world(page, x, y) {
  const c = await control(page, 'Image canvas'), s = await state(page);
  return { x: c.x + s.panX + x * s.zoom, y: c.y + s.panY + y * s.zoom };
}
async function pixel(page, point) { return decodePng(await page.screenshot()).pixel(point.x, point.y).slice(0, 3); }
const distance = (a, b) => a.reduce((sum, value, i) => sum + Math.abs(value - b[i]), 0);
async function screenshot(page, name) {
  await mkdir('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: `artifacts/screenshots/${name}.png` });
}

// Mutations below are all real pointer, keyboard, file chooser and download events.
test('Curves previews while dragging, coalesces history, handles keyboard and cancels exactly', async ({ page }) => {
  await boot(page);
  await menu(page, 'Image', 'Curves adjustment layer');
  await expect.poll(async () => (await state(page)).activeAdjustment).toBe('Curves');
  const graph = await control(page, 'Tone curve graph');
  const sample = await world(page, 500, 100);
  const original = await pixel(page, sample);
  const x = graph.x + 12 + (graph.width - 24) * 128 / 255;
  const y = graph.y + graph.height - 12 - (graph.height - 24) * 128 / 255;
  const targetY = graph.y + graph.height - 12 - (graph.height - 24) * 194 / 255;
  const history = (await state(page)).history;
  await page.mouse.move(x, y);
  await page.mouse.down();
  await page.mouse.move(x, targetY, { steps: 16 });
  await expect.poll(async () => (await state(page)).transaction).toBe(true);
  expect((await state(page)).history).toBe(history);
  await expect.poll(async () => distance(await pixel(page, sample), original), { message: 'Curves must change the live canvas before pointer release.' }).toBeGreaterThan(20);
  await page.mouse.up();
  await expect.poll(async () => (await state(page)).history).toBe(history + 1);
  expect((await state(page)).curvePoints).toBe(3);
  const midpoint = (await state(page)).curveMidpoint;
  await page.keyboard.press('ArrowUp');
  await expect.poll(async () => (await state(page)).curveMidpoint).toBe(midpoint + 1);
  expect((await state(page)).history).toBe(history + 2);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).curveMidpoint).toBe(midpoint);
  const edited = await pixel(page, sample);
  await page.mouse.move(x, targetY);
  await page.mouse.down();
  await page.mouse.move(x, graph.y + graph.height - 24, { steps: 10 });
  await expect.poll(async () => (await state(page)).transaction).toBe(true);
  await page.keyboard.press('Escape');
  await page.mouse.up();
  await expect.poll(async () => (await state(page)).transaction).toBe(false);
  expect((await state(page)).history).toBe(history + 1);
  expect((await state(page)).curveMidpoint).toBe(midpoint);
  await expect.poll(() => pixel(page, sample)).toEqual(edited);
  await screenshot(page, 'curves-adjustment');
  await click(page, 'Curves channel Red');
  await menu(page, 'Curves presets', 'Negative');
  expect((await state(page)).curveMidpoint).toBe(midpoint);
  await click(page, 'Curves channel RGB');
  await menu(page, 'Curves presets', 'Linear');
  await expect.poll(async () => (await state(page)).curvePoints).toBe(2);
});

test('Levels exposes independent channels, draggable gamma, preview and editable archive roundtrip', async ({ page }) => {
  await boot(page);
  await menu(page, 'Image', 'Levels adjustment layer');
  await expect.poll(async () => (await state(page)).activeAdjustment).toBe('Levels');
  await click(page, 'Levels channel Red');
  await click(page, 'Gamma');
  await page.keyboard.press('Control+a');
  await page.keyboard.insertText('1.60');
  await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).redGamma).toBe(1.6);
  expect((await state(page)).rgbGamma).toBe(1);
  await click(page, 'Levels channel RGB');
  const graph = await control(page, 'Levels histogram controls');
  const history = (await state(page)).history;
  const x = graph.x + 12 + (graph.width - 24) * .5;
  const y = graph.y + graph.height - 51 + 5;
  await page.mouse.move(x, y);
  await page.mouse.down();
  await page.mouse.move(graph.x + 12 + (graph.width - 24) * .25, y, { steps: 12 });
  await page.mouse.up();
  await expect.poll(async () => (await state(page)).history).toBe(history + 1);
  const gamma = (await state(page)).rgbGamma;
  expect(gamma).toBeGreaterThan(1.9);
  expect(gamma).toBeLessThan(2.1);
  expect((await state(page)).redGamma).toBe(1.6);
  await click(page, 'Adjustment preview');
  await expect.poll(async () => (await state(page)).activeVisible).toBe(false);
  await click(page, 'Adjustment preview');
  await expect.poll(async () => (await state(page)).activeVisible).toBe(true);
  await screenshot(page, 'levels-adjustment');
  const downloadPromise = page.waitForEvent('download');
  await page.keyboard.press('Control+s');
  const download = await downloadPromise;
  const file = await download.path();
  expect(download.suggestedFilename()).toMatch(/\.imagespace$/);
  const chooserPromise = page.waitForEvent('filechooser');
  await page.keyboard.press('Control+o');
  const chooser = await chooserPromise;
  await chooser.setFiles({ name: 'Levels-roundtrip.imagespace', mimeType: 'application/x-imagespace', buffer: await readFile(file) });
  await expect.poll(async () => (await state(page)).documents, { timeout: 30000 }).toBe(2);
  const restored = await state(page);
  expect(restored.activeAdjustment).toBe('Levels');
  expect(restored.redGamma).toBe(1.6);
  expect(restored.rgbGamma).toBe(gamma);
  expect(restored.layers).toBe(9);
  await screenshot(page, 'levels-roundtrip');
});
