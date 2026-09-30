// Runs inside a real browser with the shipped imageSpaceGpu implementation.
// No mock dispatches or application mutation hooks are used.
export async function tiledBlurProbe() {
  const engine = globalThis.imageSpaceGpu;
  await engine.initialize();
  if (!engine.describe().available) throw new Error(engine.describe().backend);
  const shapes = [[1, 1], [2, 67], [67, 2], [31, 33], [33, 31], [129, 65], [64, 64]];
  const sigmas = [0, .1, .3, 1, 2, 7.25, 32];
  const reports = [];
  const difference = (a, b) => {
    if (a.length !== b.length) throw new Error('Different output sizes.');
    let maximum = 0;
    for (let i = 0; i < a.length; i++) maximum = Math.max(maximum, Math.abs(a[i] - b[i]));
    return maximum;
  };
  for (const [width, height] of shapes) {
    const bytes = new Uint8Array(width * height * 4);
    const alphas = [0, 1, 2, 63, 127, 192, 254, 255];
    for (let y = 0; y < height; y++) for (let x = 0; x < width; x++)
      bytes.set([(x * 71 + y * 11) % 256, (x * 13 + y * 89) % 256,
        (x * 103 + y * 19) % 256, alphas[(x + y * 3) % alphas.length]], (y * width + x) * 4);
    const original = bytes.slice();
    let direct, tiled;
    try {
      direct = await engine.createSession(bytes, width, height, { gaussianBlur: 'direct' });
      tiled = await engine.createSession(bytes, width, height, { gaussianBlur: 'tiled' });
      if (!direct || !tiled) throw new Error('Small validation session was rejected.');
      bytes.fill(255);
      for (const amount of sigmas) {
        const recipe = [{ kind: 'GaussianBlur', amount }];
        const before = tiled.statistics();
        const [reference, output] = await Promise.all([direct.apply(recipe), tiled.apply(recipe)]);
        const after = tiled.statistics();
        reports.push({ width, height, amount, difference: difference(reference, output),
          tiledPasses: after.tiledGaussianPasses - before.tiledGaussianPasses,
          readbacks: after.readbacks - before.readbacks });
      }
      const stack = [{ kind: 'Emboss' }, { kind: 'GaussianBlur', amount: 2 },
        { kind: 'Saturation', amount: -35 }, { kind: 'Gamma', amount: 1.3 },
        { kind: 'GaussianBlur', amount: 7.25 }, { kind: 'Edges' }];
      const warm = tiled.statistics();
      await tiled.execute(stack);
      const executed = tiled.statistics();
      const actual = await tiled.read();
      const reference = await direct.apply(stack);
      const after = tiled.statistics();
      reports.push({ width, height, stack: true, difference: difference(reference, actual),
        noReadbackDuringExecute: executed.readbacks === warm.readbacks,
        unchangedAllocations: after.bufferAllocations === warm.bufferAllocations,
        sourceUploads: after.sourceUploads });
      const reset = await tiled.apply([]);
      if (difference(original, reset) !== 0) throw new Error('Tiled evaluation changed the captured source.');
      if (direct.statistics().tiledGaussianPasses !== 0 || tiled.statistics().directGaussianPasses !== 0)
        throw new Error('The forced kernel strategy was ignored.');
    } finally { direct?.dispose(); tiled?.dispose(); }
  }
  const automatic = [];
  for (const [width, height, amount, expectedTiled] of [[63, 65, 2, false], [64, 64, 1, false], [64, 64, 2, false], [129, 65, 32, false]]) {
    const source = new Uint8Array(width * height * 4);
    let auto, direct;
    try {
      auto = await engine.createSession(source, width, height);
      direct = await engine.createSession(source, width, height, { gaussianBlur: 'direct' });
      if (!auto || !direct) throw new Error('Auto validation session was rejected.');
      const recipe = [{ kind: 'GaussianBlur', amount }];
      const actual = await auto.apply(recipe);
      const expected = await direct.apply(recipe);
      automatic.push({ width, height, amount, expectedTiled, difference: difference(actual, expected), stats: auto.statistics() });
    } finally { auto?.dispose(); direct?.dispose(); }
  }
  return { adapter: engine.describe().backend, cases: reports.length, reports, automatic,
    scope: 'Actual WGSL outputs, including hidden RGB. Same-device direct/tiled comparison, not Photoshop parity or a hardware timing claim.' };
}

export async function tiledBlurBenchmark() {
  const engine = globalThis.imageSpaceGpu;
  await engine.initialize();
  if (!engine.describe().available) throw new Error(engine.describe().backend);
  const reports = [];
  for (const [width, height, sigma] of [[512, 256, 2], [512, 256, 8], [257, 129, 32]]) {
    const source = new Uint8Array(width * height * 4);
    for (let y = 0; y < height; y++) for (let x = 0; x < width; x++)
      source.set([(x * 23 + y * 17) % 256, (x * 11 + y * 53) % 256,
        (x * 37 + y * 3) % 256, (x + y) % 4 ? 255 : 17], (y * width + x) * 4);
    let direct, tiled;
    try {
      direct = await engine.createSession(source, width, height, { gaussianBlur: 'direct' });
      tiled = await engine.createSession(source, width, height, { gaussianBlur: 'tiled' });
      if (!direct || !tiled) throw new Error('Benchmark session rejected.');
      const operations = [{ kind: 'GaussianBlur', amount: sigma }];
      for (let i = 0; i < 2; i++) { await direct.apply(operations); await tiled.apply(operations); }
      const before = { direct: direct.statistics(), tiled: tiled.statistics() };
      const a = [], b = [];
      async function measure(session, samples) {
        const started = performance.now();
        await session.apply(operations);
        samples.push(performance.now() - started);
      }
      for (let i = 0; i < 7; i++) {
        if (i % 2 === 0) { await measure(direct, a); await measure(tiled, b); }
        else { await measure(tiled, b); await measure(direct, a); }
      }
      const median = values => [...values].sort((x, y) => x - y)[3];
      reports.push({ width, height, sigma, directMilliseconds: a, tiledMilliseconds: b,
        directMedian: median(a), tiledMedian: median(b), ratio: median(a) / median(b),
        before, after: { direct: direct.statistics(), tiled: tiled.statistics() } });
    } finally { direct?.dispose(); tiled?.dispose(); }
  }
  return { adapter: engine.describe().backend, warmups: 2, samples: 7, reports,
    scope: 'Same-device warm resident sessions. API completion includes dispatch, copy, mapping and output allocation. Shader load counts are algorithmic, not physical memory-traffic measurements.' };
}
