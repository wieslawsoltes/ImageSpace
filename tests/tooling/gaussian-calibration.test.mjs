import { test } from 'node:test';
import assert from 'node:assert/strict';
import { schedulingDouble } from './gpu-scheduling-double.mjs';

const source = () => new Uint8Array([13, 29, 47, 127, 83, 101, 113, 255]);
const recipe = [{ kind: 'GaussianBlur', amount: 8 }];
const options = { samples: 3, warmups: 1 };
const host = value => JSON.parse(JSON.stringify(value));

test('untuned auto retains direct kernels and compiles no tiled pipeline', async () => {
  const gpu = await schedulingDouble();
  const session = await gpu.engine.createSession(source(), 2, 1);
  try {
    assert.equal(gpu.compiled.length, 5);
    await session.apply(recipe);
    assert.equal(gpu.compiled.length, 5);
    assert.equal(session.statistics().directGaussianPasses, 2);
    assert.equal(session.statistics().tiledGaussianPasses, 0);
    assert.equal(session.gaussianCalibration(8), null);
  } finally { session.dispose(); }
});

for (const [name, directTime, tiledTime, expected, mismatch] of [
  ['slow tiled', 10, 100, 'direct', false], ['measurably faster tiled', 10, 5, 'tiled', false],
  ['marginal improvement', 10, 9, 'direct', false], ['equal', 10, 10, 'direct', false],
  ['insufficient clock resolution', 0, 0, 'direct', false], ['wrong output', 10, 5, 'direct', true]
]) test(`calibration selection policy: ${name}`, async () => {
  const gpu = await schedulingDouble({ directTime, tiledTime, mismatch });
  const session = await gpu.engine.createSession(source(), 2, 1);
  try {
    const report = await session.calibrateGaussian(8, options);
    assert.equal(report.selected, expected);
    assert.equal(report.equivalent, !mismatch);
    assert.equal(report.directMedian, directTime);
    assert.equal(report.tiledMedian, tiledTime);
    assert.ok(Object.isFrozen(report) && Object.isFrozen(report.directMilliseconds));
    const before = session.statistics();
    await session.apply(recipe);
    assert.equal(session.statistics()[expected === 'tiled' ? 'tiledGaussianPasses' : 'directGaussianPasses'] - before[expected === 'tiled' ? 'tiledGaussianPasses' : 'directGaussianPasses'], 2);
    assert.equal(gpu.compiled.length, 7);
    // A different sigma must not inherit a measurement from a different workload.
    const previous = session.statistics().directGaussianPasses;
    await session.apply([{ kind: 'GaussianBlur', amount: 9 }]);
    assert.equal(session.statistics().directGaussianPasses, previous + 2);
  } finally { session.dispose(); }
  assert.equal(gpu.engine.describe().residentBytes, 0);
});

test('noisy median alone is insufficient without paired wins', async () => {
  // One correctness run + one warmup, then five measurements in call order.
  const direct = [1, 1, 1, 1, 100, 100, 100];
  const tiled = [1, 1, 2, 2, 101, 1, 1];
  const gpu = await schedulingDouble({ directTime: () => direct.shift(), tiledTime: () => tiled.shift() });
  const session = await gpu.engine.createSession(source(), 2, 1);
  try {
    const report = await session.calibrateGaussian(8, { samples: 5 });
    assert.ok(report.directMedian > report.tiledMedian * 1.15);
    assert.equal(report.tiledWins, 2);
    assert.equal(report.selected, 'direct');
  } finally { session.dispose(); }
});

for (const stage of ['source', 'ping', 'pong']) test(`calibration preserves published ${stage} output`, async () => {
  const gpu = await schedulingDouble({ tiledTime: 5 });
  const session = await gpu.engine.createSession(source(), 2, 1);
  try {
    if (stage !== 'source') await session.apply(stage === 'ping' ? [{ kind: 'Invert' }] : [{ kind: 'Invert' }, ...recipe]);
    const before = await session.read();
    const report = await session.calibrateGaussian(8, options);
    assert.deepEqual(await session.read(), before);
    const counters = session.statistics();
    assert.equal(await session.calibrateGaussian(8, options), report);
    const after = session.statistics();
    for (const key of ['dispatches', 'readbacks', 'bufferAllocations', 'sourceUploads']) assert.equal(after[key], counters[key]);
    assert.equal(after.gaussianCalibrationHits, counters.gaussianCalibrationHits + 1);
    assert.equal(after.sourceUploads, 1);
    await session.clearGaussianCalibrations();
    assert.equal(session.gaussianCalibration(8), null);
  } finally { session.dispose(); }
});

test('aborted calibration preserves the old result and leaves no profile', async () => {
  const abort = new AbortController();
  const gpu = await schedulingDouble({ onCompleted: () => abort.abort() });
  const session = await gpu.engine.createSession(source(), 2, 1);
  try {
    const before = await session.apply([{ kind: 'Invert' }]);
    await assert.rejects(session.calibrateGaussian(8, { ...options, signal: abort.signal }), { name: 'AbortError' });
    assert.equal(session.gaussianCalibration(8), null);
    assert.deepEqual(await session.read(), before);
    assert.equal(session.statistics().disposed, false);
  } finally { session.dispose(); }
});

test('concurrent calibration requests coalesce through the serialized cache', async () => {
  const gpu = await schedulingDouble();
  const session = await gpu.engine.createSession(source(), 2, 1);
  try {
    const [a, b] = await Promise.all([session.calibrateGaussian(8, options), session.calibrateGaussian(8, options)]);
    assert.equal(a, b);
    assert.equal(session.statistics().gaussianCalibrations, 1);
    assert.equal(session.statistics().gaussianCalibrationHits, 1);
    await session.calibrateGaussian(8, { ...options, force: true });
    assert.equal(session.statistics().gaussianCalibrations, 2);
  } finally { session.dispose(); }
});

test('profiles are capped at eight entries and forcing direct bypasses learned decisions', async () => {
  const gpu = await schedulingDouble({ tiledTime: 5 });
  const session = await gpu.engine.createSession(source(), 2, 1, { gaussianBlur: 'direct' });
  try {
    for (let sigma = 1; sigma <= 9; sigma++) await session.calibrateGaussian(sigma, options);
    assert.equal(session.gaussianCalibration(1), null);
    assert.equal(session.gaussianCalibration(9).selected, 'tiled');
    const previous = session.statistics().directGaussianPasses;
    await session.apply([{ kind: 'GaussianBlur', amount: 9 }]);
    assert.equal(session.statistics().directGaussianPasses, previous + 2);
  } finally { session.dispose(); }
});

test('invalid calibration requests do not compile, allocate or submit', async () => {
  const gpu = await schedulingDouble();
  const session = await gpu.engine.createSession(source(), 2, 1);
  try {
    const before = session.statistics();
    for (const amount of [0, -1, NaN, Infinity, '8', 1000001])
      await assert.rejects(async () => session.calibrateGaussian(amount), { name: 'RangeError' });
    for (const invalid of [null, [], { samples: 2 }, { samples: 4 }, { samples: 11 }, { warmups: 0 }, { force: 1 }, { signal: {} }])
      await assert.rejects(async () => session.calibrateGaussian(8, invalid));
    assert.deepEqual(host(session.statistics()), host(before));
    assert.equal(gpu.compiled.length, 5);
  } finally { session.dispose(); }
});

test('device loss removes profiles and replacement devices initialize only base pipelines', async () => {
  const gpu = await schedulingDouble({ tiledTime: 5 });
  const device = await gpu.engine.initialize();
  const session = await gpu.engine.createSession(source(), 2, 1);
  await session.calibrateGaussian(8, options);
  assert.equal(session.gaussianCalibration(8).selected, 'tiled');
  device.destroy(); await device.lost; await Promise.resolve();
  assert.equal(session.statistics().disposed, true);
  assert.equal(gpu.engine.describe().residentBytes, 0);
  const replacement = await gpu.engine.createSession(source(), 2, 1);
  try {
    assert.equal(replacement.gaussianCalibration(8), null);
    assert.equal(gpu.engine.describe().pipelineCount, 5);
    assert.notEqual(replacement.generation, session.generation);
  } finally { replacement.dispose(); }
});
