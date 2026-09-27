import { test, expect } from '@playwright/test';
import { waitForWorkspace } from './readiness.mjs';

async function visible(page, name) {
  await expect.poll(() => page.evaluate(n => globalThis.imageSpaceControls?.some(c => c.name === n && c.enabled && c.height > 18), name)).toBe(true);
  return page.evaluate(n => globalThis.imageSpaceControls.find(c => c.name === n && c.enabled), name);
}
async function click(page, name) {
  const box = await visible(page, name);
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
}

test('the selected adjustment stays visible when its inspector reduces the layer viewport', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('./?test=1', { waitUntil: 'domcontentloaded' });
  await waitForWorkspace(page);
  await click(page, 'Image');
  await click(page, 'Levels adjustment layer');
  await expect.poll(() => page.evaluate(() => globalThis.imageSpaceDiagnostics.activeAdjustment)).toBe('Levels');
  await visible(page, 'Hide Levels');
  await expect.poll(() => page.evaluate(() => globalThis.imageSpaceControls.some(c => c.name === 'Add layer mask' && !c.enabled))).toBe(true);
  await click(page, 'Hide Levels');
  await expect.poll(() => page.evaluate(() => globalThis.imageSpaceDiagnostics.activeVisible)).toBe(false);
  await click(page, 'Show Levels');
  await expect.poll(() => page.evaluate(() => globalThis.imageSpaceDiagnostics.activeVisible)).toBe(true);
});
