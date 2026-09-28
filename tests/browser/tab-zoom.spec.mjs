import {test, expect} from '@playwright/test';
import {waitForWorkspace} from './readiness.mjs';

const state = page => page.evaluate(() => globalThis.imageSpaceDiagnostics);
async function control(page, name) {
  await expect.poll(() => page.evaluate(n => globalThis.imageSpaceControls?.some(c => c.name === n && c.enabled), name)).toBe(true);
  return page.evaluate(n => globalThis.imageSpaceControls.find(c => c.name === n && c.enabled), name);
}
async function click(page, name) {
  const c = await control(page, name);
  await page.mouse.click(c.x + c.width / 2, c.y + c.height / 2);
}
async function captionMatchesZoom(page) {
  await expect.poll(() => page.evaluate(() => {
    const s = globalThis.imageSpaceDiagnostics;
    const prefix = s.name + (s.dirty ? ' *' : '') + '  @ ';
    const tab = globalThis.imageSpaceControls?.find(c => c.type === 'StudioButton' && c.name.startsWith(prefix));
    if (!tab) return false;
    const value = Number.parseFloat(tab.name.slice(prefix.length).replace(',', '.'));
    return Math.abs(value - s.zoom * 100) <= .05001;
  }), {message:'The retained active-tab caption must display the current viewport zoom'}).toBe(true);
}

test('document tab zoom follows 100 percent, Fit and wheel input without rebuilding tabs', async ({page}) => {
  await page.goto('./?test=1', {waitUntil:'domcontentloaded'});
  await waitForWorkspace(page);
  const before = await state(page);
  await click(page, '100%');
  await expect.poll(async () => (await state(page)).zoom).toBe(1);
  await captionMatchesZoom(page);
  await click(page, 'Fit on screen');
  await expect.poll(async () => (await state(page)).zoom).not.toBe(1);
  await captionMatchesZoom(page);
  const fitted = await state(page);
  const canvas = await control(page, 'Image canvas');
  await page.mouse.move(canvas.x + canvas.width / 2, canvas.y + canvas.height / 2);
  await page.mouse.wheel(0, -240);
  await expect.poll(async () => (await state(page)).zoom).toBeGreaterThan(fitted.zoom);
  await captionMatchesZoom(page);
  const after = await state(page);
  for (const key of ['tabsCreated', 'buttonsCreated', 'inspectorBuilds', 'optionsBuilds', 'contentRevision', 'history', 'uiRefreshes'])
    expect(after[key], key).toBe(before[key]);
  expect(after.dirty).toBe(false);
  expect(after.transaction).toBe(false);
});
