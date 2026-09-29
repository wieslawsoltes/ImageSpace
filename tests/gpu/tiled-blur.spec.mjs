import { test, expect } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import { tiledBlurProbe, tiledBlurBenchmark } from './tiled-blur-probe.mjs';

test.beforeEach(async ({ page }) => {
  await page.goto('/'); await page.addScriptTag({ url: '/WebGpu.js' });
});

test('tiled Gaussian kernels match direct outputs at halo, workgroup and transparent boundaries', async ({ page }) => {
  const report = await page.evaluate(tiledBlurProbe);
  expect(report.cases).toBe(56);
  for (const item of report.reports) {
    expect(item.difference, JSON.stringify(item)).toBe(0);
    if (item.stack) {
      expect(item.noReadbackDuringExecute).toBe(true);
      expect(item.unchangedAllocations).toBe(true);
      expect(item.sourceUploads).toBe(1);
    } else {
      expect(item.tiledPasses).toBe(item.amount === 0 ? 0 : 2);
      expect(item.readbacks).toBe(1);
    }
  }
  for (const item of report.automatic) {
    expect(item.difference).toBe(0);
    expect(item.stats.tiledGaussianPasses).toBe(item.expectedTiled ? 2 : 0);
    expect(item.stats.directGaussianPasses).toBe(item.expectedTiled ? 0 : 2);
  }
  expect(await page.evaluate(() => imageSpaceGpu.describe().residentBytes)).toBe(0);
  await mkdir('artifacts', { recursive: true });
  await writeFile('artifacts/resident-gpu-tiled-parity.json', JSON.stringify(report, null, 2));
});

test('tiled blur reduces shader input loads without increasing warm allocations or readbacks', async ({ page }) => {
  const report = await page.evaluate(tiledBlurBenchmark);
  for (const item of report.reports) {
    const before = item.before, after = item.after;
    for (const mode of ['direct', 'tiled']) {
      expect(after[mode].sourceUploads).toBe(1);
      expect(after[mode].bufferAllocations).toBe(before[mode].bufferAllocations);
      expect(after[mode].bindGroupBuilds).toBe(before[mode].bindGroupBuilds);
      expect(after[mode].readbacks - before[mode].readbacks).toBe(7);
      expect(after[mode].dispatches - before[mode].dispatches).toBe(14);
    }
    const radius = Math.ceil(item.sigma * 3), { width, height } = item;
    const tiledLoads = (Math.ceil(width / 32) * Math.ceil(height / 4) +
      Math.ceil(width / 4) * Math.ceil(height / 32)) * (32 + 2 * radius) * 4;
    const directLoads = width * height * (2 * radius + 1) * 2;
    expect(after.tiled.gaussianInputReads - before.tiled.gaussianInputReads).toBe(tiledLoads * 7);
    expect(after.direct.gaussianInputReads - before.direct.gaussianInputReads).toBe(directLoads * 7);
    expect(tiledLoads).toBeLessThan(directLoads);
    // No assertion about machine-dependent speed: the emitted samples show it.
  }
  expect(await page.evaluate(() => imageSpaceGpu.describe().liveSessions)).toBe(0);
  await mkdir('artifacts', { recursive: true });
  await writeFile('artifacts/resident-gpu-tiled-performance.json', JSON.stringify(report, null, 2));
});

test('invalid Gaussian strategy is rejected before device initialization or allocation', async ({ page }) => {
  const result = await page.evaluate(async () => {
    const rejected = [];
    for (const value of ['', false, 0, null, [], {}, 'TILED']) {
      try { await imageSpaceGpu.createSession(new Uint8Array(4), 1, 1, { gaussianBlur: value }); rejected.push(false); }
      catch (error) { rejected.push(error instanceof TypeError); }
    }
    return { rejected, state: imageSpaceGpu.describe() };
  });
  expect(result.rejected).toEqual(Array(7).fill(true));
  expect(result.state.available).toBe(false);
  expect(result.state.residentBytes).toBe(0);
});

test('tiled sessions recover after device loss without retaining old-generation resources', async ({ page }) => {
  const result = await page.evaluate(async () => {
    const source = new Uint8Array(64 * 64 * 4); source.fill(127);
    const device = await imageSpaceGpu.initialize();
    const session = await imageSpaceGpu.createSession(source, 64, 64, { gaussianBlur: 'tiled' });
    if (!device || !session) throw new Error('No validation adapter.');
    await session.apply([{ kind: 'GaussianBlur', amount: 32 }]);
    device.destroy(); await device.lost;
    await Promise.resolve();
    const retired = session.statistics();
    let rejected = false;
    try { await session.read(); } catch { rejected = true; }
    const replacement = await imageSpaceGpu.createSession(source, 64, 64, { gaussianBlur: 'tiled' });
    if (!replacement) throw new Error('Replacement adapter unavailable.');
    try {
      const output = await replacement.apply([{ kind: 'GaussianBlur', amount: 8 }]);
      return { retired, rejected, newGeneration: replacement.generation !== session.generation,
        tiledPasses: replacement.statistics().tiledGaussianPasses, alpha: output[3] };
    } finally { session.dispose(); replacement.dispose(); }
  });
  expect(result.retired.disposed).toBe(true);
  expect(result.retired.residentBytes).toBe(0);
  expect(result.rejected).toBe(true); expect(result.newGeneration).toBe(true);
  expect(result.tiledPasses).toBe(2); expect(result.alpha).toBe(127);
  expect(await page.evaluate(() => imageSpaceGpu.describe().residentBytes)).toBe(0);
});
