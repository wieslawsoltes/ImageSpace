import { test, expect } from '@playwright/test';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { waitForWorkspace } from './readiness.mjs';
import { waitForControl } from './control.mjs';
import { decodePng } from './png.mjs';

const state = page => page.evaluate(() => globalThis.imageSpaceDiagnostics);
const control = (page, name) => waitForControl(page, name);
async function click(page, name) { const c = await control(page, name); await page.mouse.click(c.x + c.width / 2, c.y + c.height / 2); }
async function menu(page, name, item) { await click(page, name); await click(page, item); }
async function number(page, name, value, read) {
  await click(page, name); await page.keyboard.press('Control+a'); await page.keyboard.insertText(String(value));
  await page.keyboard.press('Enter'); await expect.poll(async () => read(await state(page))).toBeCloseTo(value, 5);
}
async function world(page, x, y) {
  const c = await control(page, 'Image canvas'), s = await state(page);
  return { x: c.x + s.panX + x * s.zoom, y: c.y + s.panY + y * s.zoom };
}
async function pixel(page, p) { return decodePng(await page.screenshot()).pixel(p.x, p.y).slice(0, 3); }
async function expectPixel(page, p, rgb) {
  await expect.poll(async () => {
    const actual = await pixel(page, p); return Math.max(...actual.map((v, i) => Math.abs(v - rgb[i])));
  }).toBeLessThanOrEqual(1);
}
async function boot(page) {
  await page.goto('./?test=1', { waitUntil: 'domcontentloaded' }); await waitForWorkspace(page);
  await click(page, 'New pixel layer'); await expect.poll(async () => (await state(page)).layers).toBe(9);
  await menu(page, 'Edit', 'Fill with foreground'); await expect.poll(async () => (await state(page)).history).toBe(2);
  await page.mouse.move(10, 10);
}
async function add(page, kind) {
  await click(page, 'New adjustment layer'); await click(page, kind === 'ChannelMixer' ? 'Channel Mixer' : kind);
  await expect.poll(async () => (await state(page)).activeAdjustment).toBe(kind);
}
function exposure(rgb, ev, offset = 0, gamma = 1) {
  return rgb.map(byte => {
    const x = byte / 255;
    const linear = x <= .04045 ? x / 12.92 : ((x + .055) / 1.055) ** 2.4;
    const corrected = Math.max(0, linear * 2 ** ev + offset) ** (1 / gamma);
    const encoded = corrected <= .0031308 ? corrected * 12.92 : 1.055 * corrected ** (1 / 2.4) - .055;
    return Math.round(Math.max(0, Math.min(1, encoded)) * 255);
  });
}
async function capture(page, name) {
  await mkdir('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: `artifacts/screenshots/${name}.png` });
}

test('Channel Mixer presets, monochrome, channel edits and native save remain editable', async ({ page }) => {
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  await boot(page); const p = await world(page, 500, 340); await expectPixel(page, p, [54, 145, 230]);
  await add(page, 'ChannelMixer');
  await menu(page, 'ChannelMixer presets', 'Swap red and blue');
  await expect.poll(async () => (await state(page)).channelMixer.Red.Blue).toBe(100);
  await page.mouse.move(10, 10); await expectPixel(page, p, [230, 145, 54]);
  await click(page, 'Monochrome'); await expect.poll(async () => (await state(page)).channelMixer.Monochrome).toBe(true);
  await expectPixel(page, p, [126, 126, 126]);
  await click(page, 'Monochrome'); await expect.poll(async () => (await state(page)).channelMixer.Monochrome).toBe(false);
  await expectPixel(page, p, [230, 145, 54]);
  await click(page, 'Mixer output Blue');
  await number(page, 'Mixer constant', 10, s => s.channelMixer.Blue.Constant);
  await page.mouse.move(10, 10); await expectPixel(page, p, [230, 145, 80]);
  await click(page, 'Move tool (V)'); await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).channelMixer.Blue.Constant).toBe(0);
  await expectPixel(page, p, [230, 145, 54]);
  await page.keyboard.press('Control+Shift+z');
  await expect.poll(async () => (await state(page)).channelMixer.Blue.Constant).toBe(10);
  const downloading = page.waitForEvent('download'); await page.keyboard.press('Control+s');
  const path = await (await downloading).path();
  const choosing = page.waitForEvent('filechooser'); await page.keyboard.press('Control+o');
  await (await choosing).setFiles({ name: 'Channel-mixer.imagespace', mimeType: 'application/x-imagespace', buffer: await readFile(path) });
  await expect.poll(async () => (await state(page)).documents).toBe(2);
  expect((await state(page)).channelMixer.Blue.Constant).toBe(10);
  const reopened = await world(page, 500, 340); await expectPixel(page, reopened, [230, 145, 80]);
  await capture(page, 'channel-mixer'); expect(errors).toEqual([]);
});

test('Exposure keeps independent fields, previews while dragging, commits once and cancels exactly', async ({ page }) => {
  await boot(page); await add(page, 'Exposure'); const p = await world(page, 500, 340);
  await number(page, 'Exposure EV', 1, s => s.exposureEV);
  await number(page, 'Exposure offset', .02, s => s.exposureOffset);
  await number(page, 'Exposure gamma', 1.2, s => s.exposureGamma);
  expect((await state(page)).exposureEV).toBe(1); expect((await state(page)).exposureOffset).toBe(.02);
  await page.mouse.move(10, 10); await expectPixel(page, p, exposure([54, 145, 230], 1, .02, 1.2));
  await click(page, 'Reset Exposure');
  await expect.poll(async () => (await state(page)).exposureEV).toBe(0);
  await expectPixel(page, p, [54, 145, 230]);
  const slider = await control(page, 'Exposure EV slider'), before = await state(page);
  const x = ev => slider.x + 7 + (slider.width - 14) * ((ev + 20) / 40), y = slider.y + slider.height / 2;
  await page.mouse.move(x(0), y); await page.mouse.down(); await page.mouse.move(x(1), y, { steps: 10 });
  await expect.poll(async () => (await state(page)).exposureEV).toBeCloseTo(1, 1);
  expect((await state(page)).history).toBe(before.history);
  expect((await state(page)).transaction).toBe(true);
  await expectPixel(page, p, exposure([54, 145, 230], (await state(page)).exposureEV));
  await page.keyboard.press('Escape'); await page.mouse.up();
  await expect.poll(async () => (await state(page)).exposureEV).toBe(0);
  expect((await state(page)).history).toBe(before.history); await expectPixel(page, p, [54, 145, 230]);
  await page.mouse.move(x(0), y); await page.mouse.down(); await page.mouse.move(x(-1), y, { steps: 10 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).history).toBe(before.history + 1);
  const after = await state(page);
  expect(after.inspectorBuilds).toBe(before.inspectorBuilds);
  expect(after.buttonsCreated).toBe(before.buttonsCreated);
  expect(after.tileUploads).toBe(before.tileUploads);
  expect(after.exposureEV).toBeCloseTo(-1, 1);
  expect(after.activeX).toBe(0); expect(after.exposureGamma).toBe(1); expect(after.exposureOffset).toBe(0);
  await page.keyboard.press('ArrowRight');
  await expect.poll(async () => (await state(page)).exposureEV).toBeCloseTo(after.exposureEV + .01, 5);
  expect((await state(page)).activeX).toBe(0);
  await capture(page, 'exposure-adjustment');
});

test('selection-created adjustment masks and explicit clipping preserve unaffected pixels', async ({ page }) => {
  await boot(page); await click(page, 'Marquee tool (M)');
  const a = await world(page, 100, 100), b = await world(page, 450, 550);
  await page.mouse.move(a.x, a.y); await page.mouse.down(); await page.mouse.move(b.x, b.y, { steps: 10 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).selection).toBe(true);
  await add(page, 'Exposure');
  expect((await state(page)).mask).toBe(true); expect((await state(page)).editMask).toBe(false);
  await number(page, 'Exposure EV', 1, s => s.exposureEV);
  await menu(page, 'Select', 'Deselect');
  const inside = await world(page, 250, 300), outside = await world(page, 700, 300);
  await page.mouse.move(10, 10);
  await expectPixel(page, inside, exposure([54, 145, 230], 1)); await expectPixel(page, outside, [54, 145, 230]);
  await menu(page, 'Layer', 'Create Clipping Mask');
  await expect.poll(async () => (await state(page)).activeClipped).toBe(true);
  await page.mouse.move(10, 10);
  await expectPixel(page, inside, exposure([54, 145, 230], 1)); await expectPixel(page, outside, [54, 145, 230]);
  await capture(page, 'masked-exposure');
});

test('warm color-inspector switching reuses controls and filters without rebuilding scene content', async ({ page }) => {
  await boot(page); await add(page, 'ChannelMixer'); await add(page, 'Exposure');
  const select = async name => { await click(page, name); await expect.poll(async () => (await state(page)).activeLayer).toBe(name); };
  await select('Channel Mixer'); await select('Exposure'); await page.waitForTimeout(400);
  const before = await state(page), samples = [];
  for (let i = 0; i < 5; i++) for (const name of ['Channel Mixer', 'Exposure']) {
    await select(name); const s = await state(page); samples.push({ target: name, synchronousRefreshMs: s.lastSelectionRefreshMs });
  }
  const after = await state(page);
  for (const key of ['buttonsCreated', 'inspectorBuilds', 'layerRowsCreated', 'optionsBuilds', 'tabsCreated',
    'colorAdjustmentFilterBuilds', 'sceneRenders', 'tileUploads', 'history', 'contentRevision', 'toneHistogramBuilds', 'channelHistogramBuilds'])
    expect(after[key], key).toBe(before[key]);
  await mkdir('artifacts/browser-tests', { recursive: true });
  await writeFile('artifacts/browser-tests/color-adjustment-responsiveness.json', JSON.stringify({ before, after, samples,
    scope: 'Synchronous retained-inspector refresh and work counters, not event-to-photon or physical GPU latency.' }, null, 2));
});
