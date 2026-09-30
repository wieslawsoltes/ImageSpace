import { test, expect } from '@playwright/test';
import { waitForWorkspace } from './readiness.mjs';
import { waitForControl } from './control.mjs';
import { decodePng } from './png.mjs';

const state = page => page.evaluate(() => globalThis.imageSpaceDiagnostics);
async function click(page, name, scope) {
  const c = await waitForControl(page, name, 20000, scope);
  await page.mouse.click(c.x + c.width / 2, c.y + c.height / 2);
}
async function menu(page, name, item) {
  await click(page, name, 'workspace');
  await click(page, item, 'popup');
}

test('Exposure preview toggles retain parameters, controls, cached filters and exact undo', async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('./?test=1', { waitUntil: 'domcontentloaded' });
  await waitForWorkspace(page);
  await click(page, 'New pixel layer');
  await expect.poll(async () => (await state(page)).layers).toBe(9);
  await menu(page, 'Edit', 'Fill with foreground');
  await expect.poll(async () => (await state(page)).history).toBe(2);
  await click(page, 'New adjustment layer');
  await click(page, 'Exposure', 'popup');
  await expect.poll(async () => (await state(page)).activeAdjustment).toBe('Exposure');
  await click(page, 'Exposure EV');
  await page.keyboard.press('Control+a');
  await page.keyboard.insertText('1');
  await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).exposureEV).toBe(1);
  const c = await waitForControl(page, 'Image canvas'), view = await state(page);
  const p = { x: c.x + view.panX + 500 * view.zoom, y: c.y + view.panY + 340 * view.zoom };
  const pixel = async () => decodePng(await page.screenshot()).pixel(p.x, p.y).slice(0, 3);
  await page.mouse.move(10, 10);
  await expect.poll(async () => (await pixel())[2]).toBe(255);
  const adjusted = await pixel(), original = [54, 145, 230], before = await state(page);
  expect(adjusted).not.toEqual(original);
  for (let i = 0; i < 2; i++) {
    await click(page, 'Color adjustment preview');
    await expect.poll(async () => (await state(page)).activeVisible).toBe(false);
    await page.mouse.move(10, 10);
    await expect.poll(pixel).toEqual(original);
    expect((await state(page)).exposureEV).toBe(1);
    await click(page, 'Color adjustment preview');
    await expect.poll(async () => (await state(page)).activeVisible).toBe(true);
    await page.mouse.move(10, 10);
    await expect.poll(pixel).toEqual(adjusted);
  }
  const after = await state(page);
  expect(after.history).toBe(before.history + 4);
  for (const key of ['buttonsCreated', 'inspectorBuilds', 'layerRowsCreated',
    'colorAdjustmentFilterBuilds', 'tileUploads', 'toneHistogramBuilds', 'channelHistogramBuilds'])
    expect(after[key], key).toBe(before[key]);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).activeVisible).toBe(false);
  await expect.poll(pixel).toEqual(original);
  await page.keyboard.press('Control+Shift+z');
  await expect.poll(async () => (await state(page)).activeVisible).toBe(true);
  await expect.poll(pixel).toEqual(adjusted);
  expect((await state(page)).exposureEV).toBe(1);
  expect(errors).toEqual([]);
});
