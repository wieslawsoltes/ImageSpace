import { test, expect } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import { fixture } from './reference.mjs';

async function boot(page) {
  await page.goto('/'); await page.addScriptTag({ url: '/WebGpu.js' });
  const capabilities = await page.evaluate(async () => { await imageSpaceGpu.initialize(); return imageSpaceGpu.describe(); });
  expect(capabilities.available, capabilities.backend).toBe(true);
  expect(capabilities.pipelineCount).toBe(5);
}

test('untuned automatic blur never chooses a tiled pipeline based only on image size', async ({ page }) => {
  await boot(page);
  const result = await page.evaluate(async bytes => {
    const source = new Uint8Array(bytes);
    const automatic = await imageSpaceGpu.createSession(source, 129, 65);
    const direct = await imageSpaceGpu.createSession(source, 129, 65, { gaussianBlur: 'direct' });
    if (!automatic || !direct) throw new Error('Validation session unavailable.');
    try {
      let maximum = 0;
      for (const amount of [2, 8, 32]) {
        const a = await automatic.apply([{ kind: 'GaussianBlur', amount }]);
        const b = await direct.apply([{ kind: 'GaussianBlur', amount }]);
        for (let i = 0; i < a.length; i++) maximum = Math.max(maximum, Math.abs(a[i] - b[i]));
      }
      return { maximum, stats: automatic.statistics(), capabilities: imageSpaceGpu.describe() };
    } finally { automatic.dispose(); direct.dispose(); }
  }, Array.from(fixture(129, 65)));
  expect(result.maximum).toBe(0);
  expect(result.stats.tiledGaussianPasses).toBe(0);
  expect(result.stats.directGaussianPasses).toBe(6);
  expect(result.stats.gaussianCalibrations).toBe(0);
  expect(result.capabilities.pipelineCount).toBe(5);
});

for (const previous of ['source', 'ping', 'pong']) test(`calibration preserves actual ${previous} output and reuses its immutable profile`, async ({ page }) => {
  await boot(page);
  const report = await page.evaluate(async ({ bytes, previous }) => {
    const session = await imageSpaceGpu.createSession(new Uint8Array(bytes), 65, 33);
    if (!session) throw new Error('Validation session unavailable.');
    try {
      const mismatch = (a, b) => a.reduce((n, value, i) => Math.max(n, Math.abs(value - b[i])), 0);
      if (previous === 'ping') await session.apply([{ kind: 'Invert' }]);
      if (previous === 'pong') await session.apply([{ kind: 'Invert' }, { kind: 'GaussianBlur', amount: 1 }]);
      const before = await session.read();
      const calibration = await session.calibrateGaussian(8, { samples: 3, warmups: 1 });
      const preserved = await session.read();
      const warm = session.statistics();
      const cached = await session.calibrateGaussian(8, { samples: 3, warmups: 1 });
      const afterCache = session.statistics();
      await session.apply([{ kind: 'GaussianBlur', amount: 8 }]);
      const selected = session.statistics();
      await session.clearGaussianCalibrations();
      await session.apply([{ kind: 'GaussianBlur', amount: 8 }]);
      return { calibration, warm, afterCache, selected, afterClear: session.statistics(),
        cachedSame: cached === calibration, preservedDifference: mismatch(before, preserved),
        immutable: Object.isFrozen(calibration) && Object.isFrozen(calibration.tiledMilliseconds),
        capabilities: imageSpaceGpu.describe(), scope: calibration.scope };
    } finally { session.dispose(); }
  }, { bytes: Array.from(fixture(65, 33)), previous });
  expect(report.preservedDifference).toBe(0);
  expect(report.calibration.equivalent).toBe(true);
  expect(report.calibration.maximumByteDifference).toBe(0);
  expect(report.immutable).toBe(true);
  expect(report.cachedSame).toBe(true);
  for (const key of ['sourceUploads', 'bufferAllocations', 'bindGroupBuilds', 'dispatches', 'readbacks'])
    expect(report.afterCache[key], key).toBe(report.warm[key]);
  expect(report.afterCache.gaussianCalibrationHits).toBe(report.warm.gaussianCalibrationHits + 1);
  const c = report.calibration;
  const shouldTile = c.directMedian > 0 && c.tiledMedian > 0 &&
    c.directMedian / c.tiledMedian >= c.minimumSpeedup && c.tiledWins >= Math.ceil(c.samples * .7);
  expect(c.selected).toBe(shouldTile ? 'tiled' : 'direct');
  const counter = shouldTile ? 'tiledGaussianPasses' : 'directGaussianPasses';
  expect(report.selected[counter] - report.afterCache[counter]).toBe(2);
  expect(report.afterClear.directGaussianPasses - report.selected.directGaussianPasses).toBe(2);
  expect(report.capabilities.pipelineCount).toBe(7);
  expect(report.afterClear.sourceUploads).toBe(1);
  expect(await page.evaluate(() => imageSpaceGpu.describe().residentBytes)).toBe(0);
  await mkdir('artifacts', { recursive: true });
  await writeFile(`artifacts/resident-gpu-calibration-${previous}.json`, JSON.stringify(report, null, 2));
});

test('pre-cancelled calibration does no optional shader work and preserves current output', async ({ page }) => {
  await boot(page);
  const result = await page.evaluate(async () => {
    const session = await imageSpaceGpu.createSession(new Uint8Array([13, 47, 101, 255]), 1, 1);
    if (!session) throw new Error('Validation session unavailable.');
    try {
      const expected = Array.from(await session.apply([{ kind: 'Invert' }]));
      const before = session.statistics();
      const controller = new AbortController(); controller.abort();
      let aborted = false;
      try { await session.calibrateGaussian(8, { signal: controller.signal }); }
      catch (error) { aborted = error.name === 'AbortError'; }
      const after = session.statistics();
      return { aborted, expected, after: Array.from(await session.read()), unchanged: JSON.stringify(before) === JSON.stringify(after),
        pipelineCount: imageSpaceGpu.describe().pipelineCount, calibration: session.gaussianCalibration(8) };
    } finally { session.dispose(); }
  });
  expect(result.aborted).toBe(true);
  expect(result.after).toEqual(result.expected);
  expect(result.unchanged).toBe(true);
  expect(result.pipelineCount).toBe(5);
  expect(result.calibration).toBeNull();
});
