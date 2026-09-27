import { test, expect } from '@playwright/test';
import { mkdir } from 'node:fs/promises';

async function visibleControl(page, name) {
  await expect.poll(() => page.evaluate(value =>
    globalThis.imageSpaceControls?.some(c => c.name === value && c.enabled && c.width > 8 && c.height > 8), name
  )).toBe(true);
  return page.evaluate(value => globalThis.imageSpaceControls.find(c => c.name === value && c.enabled), name);
}

async function click(page, name) {
  const c = await visibleControl(page, name);
  await page.mouse.click(c.x + c.width / 2, c.y + c.height / 2);
}

test('short desktop windows retain usable layer rows and creation controls', async ({ page }) => {
  await page.setViewportSize({ width: 1024, height: 640 });
  await page.goto('./?test=1', { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => globalThis.imageSpaceDiagnostics?.ready, null, { timeout: 120000 });
  await visibleControl(page, 'Hide Footer');
  const create = await visibleControl(page, 'New pixel layer');
  expect(create.y + create.height).toBeLessThanOrEqual(640);
  await click(page, 'New pixel layer');
  await expect.poll(() => page.evaluate(() => globalThis.imageSpaceDiagnostics.layers)).toBe(9);
  await visibleControl(page, 'Hide Layer 9');
  await mkdir('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/compact-workspace.png' });
});
