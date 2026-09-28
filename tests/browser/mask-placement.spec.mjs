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
  const readers = {
    'Mask X': s => s.maskX,
    'Mask Y': s => s.maskY,
    'Mask width': s => Math.hypot(s.maskM11, s.maskM12) * s.maskWidth,
    'Mask height': s => Math.hypot(s.maskM21, s.maskM22) * s.maskHeight,
    'Mask angle': s => Math.atan2(s.maskM12, s.maskM11) * 180 / Math.PI
  };
  // Read-only diagnostics are sampled, not a synchronous mutation API. Wait for
  // this specific edit before deriving coordinates for the next real pointer input.
  const read = readers[name];
  if (!read) throw new Error(`Missing numeric assertion for ${name}`);
  await expect.poll(async () => read(await state(page)), {message:`${name} must reach the requested model value`}).toBeCloseTo(value, 3);
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

test('unlinked mask moves independently, relinks without a jump, and roundtrips its affine frame', async ({ page }) => {
  await boot(page);
  const sample = await point(page, 550, 100), original = await pixel(page, sample);
  await click(page, 'Marquee tool (M)');
  await drag(page, await point(page, 600, 70), await point(page, 920, 180));
  await expect.poll(async () => (await state(page)).selection).toBe(true);
  await invertAdjustment(page); await click(page, 'Add layer mask');
  await expect.poll(async () => (await state(page)).editMask).toBe(true);
  await page.keyboard.press('Control+d');
  await click(page, 'Unlink mask for Invert');
  await expect.poll(async () => (await state(page)).maskLinked).toBe(false);
  await click(page, 'Move tool (V)');
  const before = await state(page);
  await drag(page, await point(page, 700, 100), await point(page, 600, 100));
  await page.mouse.move(10, 10);
  await expect.poll(async () => (await state(page)).history).toBe(before.history + 1);
  await expect.poll(async () => (await state(page)).maskX).toBeCloseTo(-100, 0);
  expect((await state(page)).activeX).toBe(0);
  expect((await state(page)).maskRevision).toBe(before.maskRevision);
  await expect.poll(async () => distance(await pixel(page, sample), original)).toBeGreaterThan(35);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).maskX).toBe(0);
  await expect.poll(async () => distance(await pixel(page, sample), original)).toBeLessThanOrEqual(3);
  await page.keyboard.press('Control+Shift+z');
  await expect.poll(async () => (await state(page)).maskX).toBeCloseTo(-100, 0);
  const moved = await pixel(page, sample);
  await click(page, 'Link mask for Invert');
  await expect.poll(async () => (await state(page)).maskLinked).toBe(true);
  await expect.poll(async () => distance(await pixel(page, sample), moved)).toBeLessThanOrEqual(3);
  await click(page, 'Move tool (V)'); await page.keyboard.press('Shift+ArrowRight');
  await expect.poll(async () => (await state(page)).activeX).toBe(10);
  await expect.poll(async () => (await state(page)).maskX).toBeCloseTo(-90, 0);
  const saved = await state(page);
  const downloading = page.waitForEvent('download'); await page.keyboard.press('Control+s');
  const path = await (await downloading).path();
  const choosing = page.waitForEvent('filechooser'); await page.keyboard.press('Control+o');
  await (await choosing).setFiles({name:'Placed-mask.imagespace', mimeType:'application/x-imagespace', buffer:await readFile(path)});
  await expect.poll(async () => (await state(page)).documents).toBe(2);
  const restored = await state(page);
  expect(restored.maskLinked).toBe(true);
  expect(restored.maskX).toBeCloseTo(saved.maskX, 5);
  expect(restored.activeX).toBe(10);
  await click(page, 'Edit mask for Invert');
  await click(page, 'Mask view Overlay');
  await capture(page, 'independent-mask-placement');
});

test('mask numeric affine editing and real on-canvas resizing leave authored pixels intact', async ({ page }) => {
  await boot(page); await invertAdjustment(page); await click(page, 'Add layer mask');
  await click(page, 'Unlink mask for Invert');
  await expect.poll(async () => (await state(page)).maskLinked).toBe(false);
  const revision = (await state(page)).maskRevision;
  await number(page, 'Mask width', 500); await number(page, 'Mask height', 350);
  await number(page, 'Mask angle', 30); await number(page, 'Mask X', 20); await number(page, 'Mask Y', 30);
  await click(page, 'Move tool (V)');
  const before = await state(page);
  expect(before.maskM12).toBeCloseTo(.25, 4);
  expect(before.maskX).toBeCloseTo(20, 3);
  expect(before.maskY).toBeCloseTo(30, 3);
  const toWorld = (x, y) => ({x: before.maskX + x * before.maskM11 + y * before.maskM21,
    y: before.maskY + x * before.maskM12 + y * before.maskM22});
  const corner = toWorld(before.maskWidth, before.maskHeight);
  const target = toWorld(before.maskWidth * 1.1, before.maskHeight * 1.1);
  await drag(page, await point(page,corner.x,corner.y), await point(page,target.x,target.y));
  await expect.poll(async () => (await state(page)).history).toBe(before.history + 1);
  await expect.poll(async () => (await state(page)).maskM11).toBeCloseTo(before.maskM11 * 1.1, 2);
  await expect.poll(async () => (await state(page)).maskM22).toBeCloseTo(before.maskM22 * 1.1, 2);
  await expect.poll(async () => (await state(page)).maskX).toBeCloseTo(20, 3);
  await expect.poll(async () => (await state(page)).maskY).toBeCloseTo(30, 3);
  expect((await state(page)).activeX).toBe(0);
  expect((await state(page)).maskRevision).toBe(revision);
  await click(page, 'Mask view Grayscale');
  await capture(page, 'affine-mask-controls');
});

test('full selection uses four exact contour segments without a full-image scratch buffer', async ({ page }) => {
  await boot(page); await click(page, 'Marquee tool (M)');
  await page.keyboard.press('Control+a');
  await expect.poll(async () => (await state(page)).selectionOutlineSegments).toBe(4);
  const selected = await state(page);
  expect(selected.selectionOutlineScratchBytes).toBe(selected.width * 12 + 4);
  const uploads = selected.tileUploads;
  await page.waitForTimeout(900);
  expect((await state(page)).tileUploads).toBe(uploads);
  await capture(page, 'merged-selection-outline');
  await page.keyboard.press('Control+d');
  await expect.poll(async () => (await state(page)).selectionOutlineSegments).toBe(0);
});
