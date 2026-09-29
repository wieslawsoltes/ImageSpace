import { test, expect } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import { fusionProbe, fusionBenchmark } from './fusion-probe.mjs';

test.beforeEach(async ({ page }) => {
  await page.goto('/'); await page.addScriptTag({ url: '/WebGpu.js' });
});
test('fused color stages retain every intermediate RGBA8 quantization and immutable source', async ({ page }) => {
  const report = await page.evaluate(fusionProbe);
  expect(report.cases).toBe(68);
  expect(report.reports.every(item => item.difference === 0)).toBe(true);
  expect(await page.evaluate(() => imageSpaceGpu.describe().residentBytes)).toBe(0);
  await mkdir('artifacts', { recursive: true });
  await writeFile('artifacts/resident-gpu-fusion-parity.json', JSON.stringify(report, null, 2));
});
test('fusion reduces eight color dispatches to one without new warmed allocations', async ({ page }) => {
  const report = await page.evaluate(fusionBenchmark);
  const f = report.after.fused, u = report.after.unfused;
  expect(f.dispatches - report.before.fused.dispatches).toBe(7);
  expect(u.dispatches - report.before.unfused.dispatches).toBe(56);
  expect(f.logicalOperations).toBe(u.logicalOperations);
  expect(f.bufferAllocations).toBe(report.before.fused.bufferAllocations);
  expect(f.bindGroupBuilds).toBe(report.before.fused.bindGroupBuilds);
  expect(f.sourceUploads).toBe(1); expect(u.sourceUploads).toBe(1);
  expect(await page.evaluate(() => imageSpaceGpu.describe().residentBytes)).toBe(0);
  await mkdir('artifacts', { recursive: true });
  await writeFile('artifacts/resident-gpu-fusion-performance.json', JSON.stringify(report, null, 2));
});
