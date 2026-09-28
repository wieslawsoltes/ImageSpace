/**
 * Wait for three distinct, consecutive read-only diagnostics publications with
 * identical layer-list geometry. Re-reading one stale array is not evidence of
 * stability: Uno publishes these snapshots independently of wheel animations.
 * No input is generated here, and no application state is mutated.
 */
export async function settledLayerList(page, timeout = 15000) {
  return page.evaluate(async timeout => {
    const started = performance.now();
    let previous = globalThis.imageSpaceControls;
    let signature;
    let equalSamples = 0;
    while (performance.now() - started < timeout) {
      await new Promise(resolve => requestAnimationFrame(resolve));
      const controls = globalThis.imageSpaceControls;
      if (!controls || controls === previous) continue;
      previous = controls;
      const list = controls.find(c => c.name === 'Layer list' && c.enabled && c.height > 0);
      if (!list) {
        signature = undefined;
        equalSamples = 0;
        continue;
      }
      // Bounds are already intersected with ancestor ScrollViewer clips by the
      // C# diagnostics exporter. Ignore controls outside this viewport.
      const rows = controls.filter(c => c.type === 'StudioButton' && c.enabled &&
        c.x >= list.x && c.y >= list.y &&
        c.x + c.width <= list.x + list.width + .5 &&
        c.y + c.height <= list.y + list.height + .5);
      const current = JSON.stringify({list, rows});
      equalSamples = current === signature ? equalSamples + 1 : 1;
      signature = current;
      if (equalSamples >= 3) return {list, rows};
    }
    throw new Error(`Layer-list geometry did not settle across fresh diagnostics samples within ${timeout}ms.`);
  }, timeout);
}

/** Scroll with real wheel input, then return one stable, sufficiently visible row. */
export async function revealLayer(page, name) {
  const find = view => view.rows.find(c => c.name === name && c.height >= 28);
  let view = await settledLayerList(page);
  let target = find(view);
  if (target) return target;

  await page.mouse.move(view.list.x + view.list.width / 2, view.list.y + view.list.height / 2);
  await page.mouse.wheel(0, -10000);
  view = await settledLayerList(page);
  // Each step starts from a settled viewport. Never send extra wheel input from
  // an assertion predicate or use a row sampled before that input completed.
  for (let step = 0; step < 128; step++) {
    target = find(view);
    if (target) return target;
    const before = JSON.stringify(view);
    await page.mouse.move(view.list.x + view.list.width / 2, view.list.y + view.list.height / 2);
    await page.mouse.wheel(0, Math.max(32, Math.floor(view.list.height / 2)));
    view = await settledLayerList(page);
    if (JSON.stringify(view) === before)
      throw new Error(`Layer '${name}' is not reachable in the layer list; visible controls: ${view.rows.map(c => c.name).join(', ')}.`);
  }
  throw new Error(`Layer '${name}' was not found within 128 settled wheel steps.`);
}
