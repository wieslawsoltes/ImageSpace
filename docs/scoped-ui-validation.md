# Scoped validation of retained UI commands

ImageSpace's browser harness drives real pointer/keyboard/file input using opt-in, read-only Uno visual-tree diagnostics. It has no endpoint for mutating the C# document model. A control's name alone is not always sufficient to locate an intended command: the Marquee toolbar and Select menu both expose **Deselect**.

## Scope and stability

`CaptureControls` reports `scope: "popup"` for controls reached from an open popup root and `scope: "workspace"` for the ordinary workspace. Popup roots are visited first so a popup child encountered again through the main visual tree is not mislabeled. This is logical containment metadata, not a replacement for full operating-system hit testing or arbitrary overlapping-window occlusion detection.

```javascript
const item = await waitForControl(page, 'Deselect', 20000, 'popup');
await page.mouse.click(item.x + item.width / 2, item.y + item.height / 2);
await expect.poll(async () => (await state(page)).selection).toBe(false);
```

The fourth argument is optional. Existing calls remain supported. A specified scope cannot fall back to a disabled, missing or differently scoped control. The observer still requires two fresh diagnostic publications with matching type, scope and bounds; it never replays an edit. After sending input, tests must assert the intended model effect and, where relevant, the presented pixels. Finding a button is not evidence that the command ran.

Five additional dependency-free contracts cover same-name popup/workspace controls, disabled/missing popup commands, transitions between scopes and invalid arguments. The Channel Mixer/Exposure mask scenario requires deselection and one history entry before explicitly creating a clipping mask; clipping must add one history entry without rewriting the authored mask. This catches the original duplicate-label failure rather than extending sleeps or removing assertions.

## Retained preview labels

The color-adjustment inspector now owns its preview caption as a retained TextBlock. Refresh assigns the caption only when visibility changes. The button's semantic name and tooltip remain fixed rather than being rewritten to the decorated caption and immediately restored on every selection. This removes redundant property updates; it is not a quantified whole-editor speedup.

A real-input browser regression toggles Exposure preview, verifies exact original/adjusted screen pixels and undo/redo, and requires unchanged parameters, control-construction counters, cached color-filter builds, tile uploads and hidden histogram counts. Existing warm inspector switching and slider gesture tests remain in the full suite.

Run `npm run test:browser-helpers` for the dependency-free contracts and `npm run test:browser` after a real Uno publish for interaction tests. Node observation tests do not execute Uno or certify GPU behavior. Per-commit CI reports establish which checks actually ran.
