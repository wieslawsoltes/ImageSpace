/** Runs against the actual initialized module; usable from browser/Playwright without .NET mutation hooks. */
export async function fusionProbe() {
  const engine = globalThis.imageSpaceGpu;
  const width = 19, height = 17, source = new Uint8Array(width * height * 4);
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++)
    source.set([(x * 17 + y * 37) % 256, (x * 37 + y * 13) % 256,
      (x * 71 + y * 19) % 256, [0, 1, 64, 127, 255][(x + y) % 5]], (y * width + x) * 4);
  const operations = [{ kind: 'Invert' }, { kind: 'Grayscale' }, { kind: 'Sepia' },
    { kind: 'BrightnessContrast', amount: 7.5, secondary: 13 }, { kind: 'Saturation', amount: -31 },
    { kind: 'Gamma', amount: 1.43 }, { kind: 'Threshold', amount: 120.5 }, { kind: 'Posterize', amount: 7 }];
  const fused = await engine.createSession(source, width, height);
  const separate = await engine.createSession(source, width, height, { fuseColorOperations: false });
  if (!fused || !separate) throw new Error(engine.describe().backend);
  const reports = [];
  try {
    const recipes = [];
    for (const a of operations) for (const b of operations) recipes.push([a, b]);
    recipes.push([...operations, ...operations], [operations[0], { kind: 'GaussianBlur', amount: .5 }, operations[2], operations[3]],
      [operations[4], operations[5], { kind: 'Sharpen', amount: .5 }, operations[0], operations[2]],
      [{ kind: 'Emboss' }, operations[1], operations[2], { kind: 'Pixelate', amount: 3 }, operations[0], operations[5]]);
    for (const recipe of recipes) {
      const a = await fused.apply(recipe), b = await separate.apply(recipe);
      let difference = 0;
      for (let i = 0; i < a.length; i++) difference = Math.max(difference, Math.abs(a[i] - b[i]));
      reports.push({ recipe, difference });
      if (difference !== 0) throw new Error('Fusion changed RGBA8 semantics: ' + JSON.stringify(reports.at(-1)));
    }
    const reset = await fused.apply([]);
    for (let i = 0; i < reset.length; i++) if (reset[i] !== source[i]) throw new Error('Reset changed the immutable source.');
    const before = fused.statistics();
    await fused.apply(operations);
    const warmed = fused.statistics();
    await fused.apply(operations);
    const after = fused.statistics();
    if (after.dispatches !== warmed.dispatches + 1 || after.logicalOperations !== warmed.logicalOperations + 8 ||
        after.fusedPasses !== warmed.fusedPasses + 1 || after.bufferAllocations !== warmed.bufferAllocations ||
        after.bindGroupBuilds !== warmed.bindGroupBuilds || after.sourceUploads !== 1)
      throw new Error('Fusion resource or work invariant failed.');
    return { cases: reports.length, reports, before, warmed, after, adapter: engine.describe().backend };
  } finally { fused.dispose(); separate.dispose(); }
}

export async function fusionBenchmark() {
  const engine = globalThis.imageSpaceGpu, width = 512, height = 512;
  const source = new Uint8Array(width * height * 4);
  for (let i = 0; i < source.length; i += 4) source.set([61, 117, 183, 255], i);
  const recipe = [{ kind: 'Invert' }, { kind: 'Gamma', amount: 1.1 }, { kind: 'BrightnessContrast', amount: 5, secondary: 8 },
    { kind: 'Saturation', amount: -30 }, { kind: 'Posterize', amount: 32 }, { kind: 'Sepia' }, { kind: 'Grayscale' }, { kind: 'Gamma', amount: 1.2 }];
  const fused = await engine.createSession(source, width, height);
  const unfused = await engine.createSession(source, width, height, { fuseColorOperations: false });
  if (!fused || !unfused) throw new Error(engine.describe().backend);
  const fast = [], slow = [];
  const measure = async (session, samples) => { const start = performance.now(); await session.apply(recipe); samples.push(performance.now() - start); };
  try {
    for (let i = 0; i < 2; i++) { await fused.apply(recipe); await unfused.apply(recipe); }
    const before = { fused: fused.statistics(), unfused: unfused.statistics() };
    for (let i = 0; i < 7; i++) {
      if (i % 2) { await measure(fused, fast); await measure(unfused, slow); }
      else { await measure(unfused, slow); await measure(fused, fast); }
    }
    const median = values => [...values].sort((a, b) => a - b)[3];
    return { width, height, operations: recipe.length, adapter: engine.describe().backend, warmups: 2, samples: 7,
      fusedMilliseconds: fast, unfusedMilliseconds: slow, fusedMedian: median(fast), unfusedMedian: median(slow),
      ratio: median(slow) / median(fast), before, after: { fused: fused.statistics(), unfused: unfused.statistics() },
      scope: 'Same-device warmed resident sessions; API completion including final readback. Not isolated GPU timestamps or whole-editor frame latency.' };
  } finally { fused.dispose(); unfused.dispose(); }
}
