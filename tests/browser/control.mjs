/**
 * Read one settled, actionable control from fresh diagnostic publications.
 * Checking visibility and then looking up the control in a second snapshot is
 * racy, especially when consecutive popups contain the same item labels.
 * This observer sends no input and never changes application state.
 */
export async function waitForControl(page, name, timeout = 20000) {
  if (typeof name !== 'string' || !name.length || !Number.isFinite(timeout) || timeout <= 0)
    throw new TypeError('A control name and positive finite timeout are required.');
  return page.evaluate(async ({name, timeout}) => {
    const started = performance.now();
    let previous = globalThis.imageSpaceControls;
    let signature;
    let matches = 0;
    while (performance.now() - started < timeout) {
      await new Promise(resolve => requestAnimationFrame(resolve));
      const controls = globalThis.imageSpaceControls;
      if (!Array.isArray(controls) || controls === previous) continue;
      previous = controls;
      const control = controls.find(c => c.name === name && c.enabled &&
        Number.isFinite(c.x) && Number.isFinite(c.y) &&
        Number.isFinite(c.width) && Number.isFinite(c.height) && c.width > 1 && c.height > 1);
      if (!control) { signature = undefined; matches = 0; continue; }
      const next = JSON.stringify([control.type, control.x, control.y, control.width, control.height]);
      matches = signature === next ? matches + 1 : 1;
      signature = next;
      if (matches >= 2) return {...control};
    }
    throw new Error(`Control '${name}' did not settle across fresh diagnostics within ${timeout}ms.`);
  }, {name, timeout});
}
