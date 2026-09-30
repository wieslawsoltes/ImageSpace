import { test } from 'node:test';
import assert from 'node:assert/strict';
import { schedulingDouble } from './gpu-scheduling-double.mjs';

// Tests the shipped JS scheduling and parameter preparation, NOT WGSL or hardware.
const bytes = () => new Uint8Array([13, 29, 47, 127, 83, 101, 113, 255]);
const recipe = (sigma = 8, brightness = 5) => [
  { kind: 'GaussianBlur', amount: sigma },
  { kind: 'BrightnessContrast', amount: brightness, secondary: 12 },
  { kind: 'Gamma', amount: 1.25 }
];
const options = { samples: 3, warmups: 1 };
const json = value => JSON.parse(JSON.stringify(value));

test('250 repeated stack evaluations prepare one plan and one coefficient table', async () => {
  const gpu = await schedulingDouble();
  const session = await gpu.engine.createSession(bytes(), 2, 1);
  try {
    await session.apply(recipe());
    const warm = session.statistics();
    for (let i = 0; i < 250; i++) await session.execute(recipe());
    const after = session.statistics();
    assert.equal(after.executionPlanBuilds, 1);
    assert.equal(after.executionPlanHits, 250);
    assert.equal(after.gaussianWeightBuilds, 1);
    assert.equal(after.executionPlansCached, 1);
    assert.equal(after.cachedParameterBytes, 2048);
    assert.equal(after.dispatches - warm.dispatches, 750);
    assert.equal(after.readbacks, warm.readbacks);
    assert.equal(after.sourceUploads, 1);
    assert.equal(after.bufferAllocations, warm.bufferAllocations);
    assert.equal(after.bindGroupBuilds, warm.bindGroupBuilds);
  } finally { session.dispose(); }
  assert.equal(session.statistics().cachedParameterBytes, 0);
  assert.equal(gpu.engine.describe().residentBytes, 0);
});

test('editing color parameters reuses unchanged Gaussian coefficients, not output pixels', async () => {
  const gpu = await schedulingDouble({ captureUniforms: true });
  const session = await gpu.engine.createSession(bytes(), 2, 1);
  try {
    for (const amount of [1, 2, 3, 4]) await session.execute(recipe(8, amount));
    const stats = session.statistics();
    assert.equal(stats.executionPlanBuilds, 4);
    assert.equal(stats.gaussianWeightBuilds, 1);
    assert.equal(stats.gaussianWeightHits, 3);
    assert.equal(stats.dispatches, 12);
    for (let i = 0; i < 4; i++) {
      const uniform = new Float32Array(gpu.uniformWrites[i].buffer, 1024);
      // The fused run begins at the second slot; operation amount is float 9.
      assert.equal(uniform[9], i + 1);
      assert.equal(uniform[10], 12);
    }
  } finally { session.dispose(); }
});

test('cached parameter bytes survive intervening recipes and private arena reuse', async () => {
  const gpu = await schedulingDouble({ captureUniforms: true });
  const session = await gpu.engine.createSession(bytes(), 2, 1);
  try {
    await session.execute(recipe(8, 5));
    const original = gpu.uniformWrites[0].slice();
    await session.execute(recipe(32, 57));
    await session.execute([{ kind: 'Invert' }]);
    await session.execute(recipe(8, 5));
    assert.deepEqual(gpu.uniformWrites.at(-1), original);
    assert.equal(session.statistics().executionPlanBuilds, 3);
    assert.equal(session.statistics().executionPlanHits, 1);
  } finally { session.dispose(); }
});

test('coefficient table matches the uncached double-normalize/float-store writer byte for byte', async () => {
  const gpu = await schedulingDouble({ captureUniforms: true });
  const session = await gpu.engine.createSession(bytes(), 2, 1);
  try {
    for (const amount of [.1, .3, 1, 2, 7.25, 8, 31.1, 32]) {
      await session.execute([{ kind: 'GaussianBlur', amount }]);
      const radius = Math.ceil(amount * 3), raw = [];
      let sum = 0;
      for (let i = -radius; i <= radius; i++) {
        const weight = Math.exp(-i * i / (2 * amount * amount));
        raw.push(weight); sum += weight;
      }
      const reference = Float32Array.from(raw, value => value / sum);
      const upload = gpu.uniformWrites.at(-1);
      const actual = new Uint8Array(upload.buffer, 32, reference.byteLength);
      assert.deepEqual(actual, new Uint8Array(reference.buffer));
    }
  } finally { session.dispose(); }
});

test('multiple Gaussian stages in one recipe share coefficients without reordering dispatches', async () => {
  const gpu = await schedulingDouble();
  const session = await gpu.engine.createSession(bytes(), 2, 1);
  try {
    await session.execute([
      { kind: 'GaussianBlur', amount: 8 }, { kind: 'Invert' }, { kind: 'GaussianBlur', amount: 8 }
    ]);
    assert.deepEqual(gpu.submitted, ['horizontal', 'vertical', 'point', 'horizontal', 'vertical']);
    assert.equal(session.statistics().gaussianWeightBuilds, 1);
    assert.equal(session.statistics().gaussianWeightHits, 1);
  } finally { session.dispose(); }
});

test('plan and coefficient caches are LRU-bounded and report exact retained parameter bytes', async () => {
  const gpu = await schedulingDouble();
  const session = await gpu.engine.createSession(bytes(), 2, 1);
  try {
    for (let sigma = 1; sigma <= 8; sigma++) await session.execute(recipe(sigma));
    await session.execute(recipe(1)); // Protect the original plan from the next eviction.
    await session.execute(recipe(9));
    assert.equal(session.statistics().executionPlansCached, 8);
    assert.equal(session.statistics().gaussianWeightsCached, 8);
    assert.equal(session.statistics().cachedParameterBytes, 8 * 2048);
    const before = session.statistics();
    await session.execute(recipe(1));
    assert.equal(session.statistics().executionPlanBuilds, before.executionPlanBuilds);
    await session.execute(recipe(2)); // Plan 2 was least recently used.
    assert.equal(session.statistics().executionPlanBuilds, before.executionPlanBuilds + 1);
    assert.equal(session.statistics().executionPlansCached, 8);
  } finally { session.dispose(); }
  assert.equal(session.statistics().executionPlansCached, 0);
  assert.equal(session.statistics().gaussianWeightsCached, 0);
});

test('captured operations cannot be changed by their caller while evaluation is queued', async () => {
  const gpu = await schedulingDouble({ captureUniforms: true });
  const session = await gpu.engine.createSession(bytes(), 2, 1);
  try {
    const operations = recipe(8, 11);
    const pending = session.execute(operations);
    operations[0].amount = 32;
    operations[1].amount = 97;
    operations.reverse();
    await pending;
    assert.equal(new Float32Array(gpu.uniformWrites[0].buffer)[4], 8);
    assert.equal(new Float32Array(gpu.uniformWrites[0].buffer, 1024)[9], 11);
    await session.execute(recipe(8, 11));
    assert.equal(session.statistics().executionPlanHits, 1);
  } finally { session.dispose(); }
});

test('disabled and no-op steps normalize to the same cached effective plan', async () => {
  const gpu = await schedulingDouble();
  const session = await gpu.engine.createSession(bytes(), 2, 1);
  try {
    await session.execute(recipe());
    await session.execute([{ kind: 'GaussianBlur', amount: 0 },
      { kind: 'Noise', amount: 40, enabled: false }, ...recipe()]);
    assert.equal(session.statistics().executionPlanBuilds, 1);
    assert.equal(session.statistics().executionPlanHits, 1);
    const before = session.statistics();
    const reset = await session.apply([]);
    assert.deepEqual(reset, bytes());
    assert.equal(session.statistics().executionPlanBuilds, before.executionPlanBuilds);
    assert.equal(session.statistics().dispatches, before.dispatches);
  } finally { session.dispose(); }
});

test('calibration and explicit clearing invalidate previously prepared automatic decisions', async () => {
  const gpu = await schedulingDouble({ tiledTime: 5 });
  const session = await gpu.engine.createSession(bytes(), 2, 1);
  const op = [{ kind: 'GaussianBlur', amount: 8 }];
  try {
    await session.execute(op);
    assert.equal(session.statistics().directGaussianPasses, 2);
    assert.equal((await session.calibrateGaussian(8, options)).selected, 'tiled');
    assert.equal(session.statistics().executionPlansCached, 0);
    const before = session.statistics();
    await session.execute(op);
    assert.equal(session.statistics().tiledGaussianPasses - before.tiledGaussianPasses, 2);
    await session.clearGaussianCalibrations();
    assert.equal(session.statistics().executionPlansCached, 0);
    const afterClear = session.statistics();
    await session.execute(op);
    assert.equal(session.statistics().directGaussianPasses - afterClear.directGaussianPasses, 2);
  } finally { session.dispose(); }
});

test('a source calibration never selects tiled for a Gaussian whose input is a preceding effect', async () => {
  const gpu = await schedulingDouble({ tiledTime: 5 });
  const session = await gpu.engine.createSession(bytes(), 2, 1);
  try {
    assert.equal((await session.calibrateGaussian(8, options)).selected, 'tiled');
    const before = session.statistics();
    await session.execute([{ kind: 'Invert' }, { kind: 'GaussianBlur', amount: 8 }]);
    assert.equal(session.statistics().directGaussianPasses - before.directGaussianPasses, 2);
    assert.equal(session.statistics().tiledGaussianPasses, before.tiledGaussianPasses);
    const start = session.statistics();
    await session.execute([{ kind: 'GaussianBlur', amount: 8 }, { kind: 'GaussianBlur', amount: 8 }]);
    assert.equal(session.statistics().tiledGaussianPasses - start.tiledGaussianPasses, 2);
    assert.equal(session.statistics().directGaussianPasses - start.directGaussianPasses, 2);
  } finally { session.dispose(); }
});

test('forced tiled remains a deliberate override for spatial filters after other operations', async () => {
  const gpu = await schedulingDouble();
  const session = await gpu.engine.createSession(bytes(), 2, 1, { gaussianBlur: 'tiled' });
  try {
    await session.execute([{ kind: 'Invert' }, { kind: 'GaussianBlur', amount: 8 }]);
    assert.equal(session.statistics().directGaussianPasses, 0);
    assert.equal(session.statistics().tiledGaussianPasses, 2);
  } finally { session.dispose(); }
});

test('sessions cannot share prepared pixel sources or coefficient cache ownership', async () => {
  const gpu = await schedulingDouble();
  const a = await gpu.engine.createSession(bytes(), 2, 1);
  const other = new Uint8Array([1, 2, 3, 255, 4, 5, 6, 255]);
  const b = await gpu.engine.createSession(other, 2, 1);
  try {
    await a.execute(recipe()); await b.execute(recipe());
    assert.equal(a.statistics().executionPlanBuilds, 1);
    assert.equal(b.statistics().executionPlanBuilds, 1);
    assert.notDeepEqual(await a.apply([]), await b.apply([]));
    a.dispose();
    assert.equal(b.statistics().executionPlansCached, 1);
    await b.execute(recipe());
    assert.equal(b.statistics().executionPlanHits, 1);
  } finally { a.dispose(); b.dispose(); }
});

test('device loss clears every retained preparation cache and cannot poison a replacement session', async () => {
  const gpu = await schedulingDouble();
  const device = await gpu.engine.initialize();
  const session = await gpu.engine.createSession(bytes(), 2, 1);
  await session.execute(recipe());
  device.destroy(); await device.lost; await Promise.resolve();
  assert.equal(session.statistics().executionPlansCached, 0);
  assert.equal(session.statistics().gaussianWeightsCached, 0);
  assert.equal(session.statistics().cachedParameterBytes, 0);
  const replacement = await gpu.engine.createSession(bytes(), 2, 1);
  try {
    await replacement.execute(recipe());
    assert.equal(replacement.statistics().executionPlanBuilds, 1);
    assert.equal(replacement.statistics().executionPlanHits, 0);
  } finally { replacement.dispose(); }
});

test('invalid operations do not mutate warmed preparation caches', async () => {
  const gpu = await schedulingDouble();
  const session = await gpu.engine.createSession(bytes(), 2, 1);
  try {
    await session.execute(recipe());
    const before = json(session.statistics());
    await assert.rejects(async () => session.execute([{ kind: 'Gamma', amount: NaN }]));
    assert.deepEqual(json(session.statistics()), before);
  } finally { session.dispose(); }
});
