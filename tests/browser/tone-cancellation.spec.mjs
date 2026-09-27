import { test, expect } from '@playwright/test';
import { waitForWorkspace } from './readiness.mjs';

const state = page => page.evaluate(() => globalThis.imageSpaceDiagnostics);
async function control(page, name) {
  await expect.poll(() => page.evaluate(n => globalThis.imageSpaceControls?.some(c => c.name === n && c.enabled), name)).toBe(true);
  return page.evaluate(n => globalThis.imageSpaceControls.find(c => c.name === n && c.enabled), name);
}
async function click(page, name) {
  const box = await control(page, name);
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
}
async function add(page, kind) {
  await click(page, 'Image');
  await click(page, kind + ' adjustment layer');
  await expect.poll(async () => (await state(page)).activeAdjustment).toBe(kind);
}

test('global undo during a tonal drag releases capture and cannot mutate the restored document', async ({ page }) => {
  await page.goto('./?test=1', { waitUntil: 'domcontentloaded' });
  await waitForWorkspace(page);
  await add(page, 'Curves');
  const curve = await control(page, 'Tone curve graph');
  const history = (await state(page)).history;
  await page.mouse.move(curve.x + curve.width / 2, curve.y + curve.height / 2);
  await page.mouse.down();
  await page.mouse.move(curve.x + curve.width / 2, curve.y + 24, { steps: 10 });
  await expect.poll(async () => (await state(page)).transaction).toBe(true);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).transaction).toBe(false);
  await page.mouse.move(curve.x + curve.width * .7, curve.y + curve.height * .8, { steps: 12 });
  await page.mouse.up();
  await expect.poll(async () => (await state(page)).curvePoints).toBe(2);
  expect((await state(page)).curveMidpoint).toBe(128);
  expect((await state(page)).history).toBe(history);

  await add(page, 'Levels');
  const levels = await control(page, 'Levels histogram controls');
  const nextHistory = (await state(page)).history;
  const y = levels.y + levels.height - 46;
  await page.mouse.move(levels.x + levels.width / 2, y);
  await page.mouse.down();
  await page.mouse.move(levels.x + levels.width * .25, y, { steps: 10 });
  await expect.poll(async () => (await state(page)).rgbGamma).toBeGreaterThan(1.5);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).transaction).toBe(false);
  await page.mouse.move(levels.x + levels.width * .8, y, { steps: 10 });
  await page.mouse.up();
  await expect.poll(async () => (await state(page)).rgbGamma).toBe(1);
  expect((await state(page)).history).toBe(nextHistory);
});
