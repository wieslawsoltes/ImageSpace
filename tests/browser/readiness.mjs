import { expect } from '@playwright/test';

/** Wait for the genuine Uno workspace, not just an initialized C# model behind the bootstrap overlay. */
export async function waitForWorkspace(page) {
  await page.waitForFunction(() => globalThis.imageSpaceDiagnostics?.ready, null, { timeout: 120000 });
  await page.waitForFunction(() => globalThis.imageSpaceControls?.some(c =>
    c.name === 'Image canvas' && c.width > 100 && c.height > 100), null, { timeout: 30000 });
  await page.locator('.uno-loader').waitFor({ state: 'hidden', timeout: 120000 });
  await expect.poll(() => page.evaluate(() => globalThis.imageSpaceDiagnostics?.tileUploads || 0), {
    timeout: 30000,
    message: 'The real Skia viewport must upload and draw its document tiles.'
  }).toBeGreaterThan(0);
  await page.evaluate(async () => {
    await document.fonts.ready;
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  });
  const before = await page.evaluate(() => {
    const state = globalThis.imageSpaceDiagnostics;
    return [state.zoom, state.panX, state.panY];
  });
  await expect.poll(() => page.evaluate(() => {
    const state = globalThis.imageSpaceDiagnostics;
    return [state.zoom, state.panX, state.panY];
  })).toEqual(before);
}
