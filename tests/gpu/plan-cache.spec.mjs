import { test, expect } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import { fixture } from './reference.mjs';

test.beforeEach(async ({ page }) => {
  await page.goto('/'); await page.addScriptTag({ url: '/WebGpu.js' });
  const state = await page.evaluate(async () => { await imageSpaceGpu.initialize(); return imageSpaceGpu.describe(); });
  expect(state.available, state.backend).toBe(true);
});

test('cached dispatch plans remain byte-identical to fresh sessions through parameter edits and eviction', async ({ page }) => {
  const result = await page.evaluate(async bytes => {
    const source = new Uint8Array(bytes), reports = [];
    const session = await imageSpaceGpu.createSession(source, 37, 19);
    if (!session) throw new Error('Validation session unavailable.');
    const op = (sigma, amount) => [
      { kind: 'GaussianBlur', amount: sigma },
      { kind: 'BrightnessContrast', amount, secondary: 17 }, { kind: 'Gamma', amount: 1.2 }
    ];
    const recipes = [op(2, 5), op(2, 11), op(8, 5), op(2, 5),
      ...Array.from({ length: 10 }, (_, i) => op(2, 20 + i)), op(2, 5), op(8, 5)];
    try {
      for (const operations of recipes) {
        const actual = await session.apply(operations);
        const expected = await imageSpaceGpu.applyChain(source, 37, 19, operations);
        if (!expected) throw new Error('Fresh reference unavailable.');
        let difference = 0;
        for (let i = 0; i < expected.length; i++) difference = Math.max(difference, Math.abs(expected[i] - actual[i]));
        reports.push({ operations, difference });
      }
      const stats = session.statistics();
      const empty = await session.apply([]);
      return { reports, stats, reset: empty.every((value, i) => value === source[i]) };
    } finally { session.dispose(); }
  }, Array.from(fixture(37, 19)));
  expect(result.reports).toHaveLength(16);
  for (const report of result.reports) expect(report.difference, JSON.stringify(report.operations)).toBe(0);
  expect(result.stats.executionPlanHits).toBeGreaterThan(0);
  expect(result.stats.executionPlansCached).toBe(8);
  expect(result.stats.cachedParameterBytes).toBe(8 * 2048);
  expect(result.stats.gaussianWeightBuilds).toBe(2);
  expect(result.reset).toBe(true);
  expect(await page.evaluate(() => imageSpaceGpu.describe().residentBytes)).toBe(0);
});

test('warm mixed stacks reuse preparation and resources while executing and reading real pixels', async ({ page }) => {
  const result = await page.evaluate(async bytes => {
    const session = await imageSpaceGpu.createSession(new Uint8Array(bytes), 65, 33);
    if (!session) throw new Error('Validation session unavailable.');
    const operations = [{ kind: 'GaussianBlur', amount: 2 }, { kind: 'Gamma', amount: 1.3 },
      { kind: 'Saturation', amount: -45 }, { kind: 'GaussianBlur', amount: 8 }, { kind: 'Emboss' }];
    try {
      const reference = await session.apply(operations), before = session.statistics();
      let difference = 0;
      for (let n = 0; n < 5; n++) {
        const actual = await session.apply(operations);
        for (let i = 0; i < reference.length; i++) difference = Math.max(difference, Math.abs(reference[i] - actual[i]));
      }
      return { before, after: session.statistics(), difference,
        scope: 'Shipped real-WebGPU execution and bounded host-side preparation counters. No GPU-time or wall-clock speed claim.' };
    } finally { session.dispose(); }
  }, Array.from(fixture(65, 33)));
  expect(result.difference).toBe(0);
  for (const key of ['executionPlanBuilds', 'gaussianWeightBuilds', 'sourceUploads', 'bufferAllocations', 'bindGroupBuilds'])
    expect(result.after[key], key).toBe(result.before[key]);
  expect(result.after.executionPlanHits - result.before.executionPlanHits).toBe(5);
  expect(result.after.readbacks - result.before.readbacks).toBe(5);
  expect(result.after.dispatches - result.before.dispatches).toBe(30);
  expect(await page.evaluate(() => imageSpaceGpu.describe().liveSessions)).toBe(0);
  await mkdir('artifacts', { recursive: true });
  await writeFile('artifacts/resident-gpu-preparation-cache.json', JSON.stringify(result, null, 2));
});

test('calibrated source policy is not extrapolated to transformed intermediate filter inputs', async ({ page }) => {
  const result = await page.evaluate(async bytes => {
    const session = await imageSpaceGpu.createSession(new Uint8Array(bytes), 65, 33);
    if (!session) throw new Error('Validation session unavailable.');
    try {
      await session.execute([{ kind: 'GaussianBlur', amount: 8 }]);
      const profile = await session.calibrateGaussian(8, { samples: 3, warmups: 1 });
      const cleared = session.statistics().executionPlansCached;
      const before = session.statistics();
      await session.apply([{ kind: 'Invert' }, { kind: 'GaussianBlur', amount: 8 }]);
      const afterPrefix = session.statistics();
      await session.apply([{ kind: 'GaussianBlur', amount: 8 }]);
      const afterSource = session.statistics();
      await session.clearGaussianCalibrations();
      const plansAfterClear = session.statistics().executionPlansCached;
      await session.apply([{ kind: 'GaussianBlur', amount: 8 }]);
      return { profile, cleared, before, afterPrefix, afterSource, plansAfterClear, afterClear: session.statistics() };
    } finally { session.dispose(); }
  }, Array.from(fixture(65, 33)));
  expect(result.cleared).toBe(0);
  expect(result.plansAfterClear).toBe(0);
  expect(result.afterPrefix.directGaussianPasses - result.before.directGaussianPasses).toBe(2);
  expect(result.afterPrefix.tiledGaussianPasses).toBe(result.before.tiledGaussianPasses);
  const counter = result.profile.selected === 'tiled' ? 'tiledGaussianPasses' : 'directGaussianPasses';
  expect(result.afterSource[counter] - result.afterPrefix[counter]).toBe(2);
  expect(result.afterClear.directGaussianPasses - result.afterSource.directGaussianPasses).toBe(2);
  expect(await page.evaluate(() => imageSpaceGpu.describe().residentBytes)).toBe(0);
});
