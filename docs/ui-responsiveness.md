# Selection and UI responsiveness

## Removed work from the selection path

Previously, `EditorSession.Changed` rebuilt the tabs, every layer row and layer settings, most property controls, hidden History/Channels panels and the tool-options bar. Each newly created `StudioButton` also reparsed its runtime XAML control template. Tone inspectors synchronously rasterized their input histogram; thumbnails took tile snapshots during drawing. Selection advanced the same revision used to decide whether to compress a complete recovery archive.

The workbench now keeps its live controls and changes their values. Selection is not debounced: the selected row and property fields update synchronously. Only optional histogram rasterization is deferred.

| Component | Invalidation and ownership |
| --- | --- |
| Buttons | One parsed `ControlTemplate` per UI thread, with separate instantiated visual trees/namescopes; unchanged selection appearance is not rewritten |
| Layer list | Rows keyed by layer ID; only additions/removals/reordering change the child collection; selection updates the previous/current row and retained settings |
| Inspector | One retained editor per schema, not per layer; current layer is resolved after undo/redo instead of capturing stale model objects |
| Curves/Levels | One editor per tone kind; channel preference stored separately from controls; transaction cancellation remains explicit |
| Masks | One reusable inspector with in-place density/feather/affine fields and original editing behavior |
| Tool options | One lazily constructed group per tool; values refresh in place, with session-independent command callbacks |
| Tabs | One retained tab per open session; labels/dirty state update without replacing buttons |
| History | Created only when opened; unchanged history prefixes are retained; jumping through history refreshes the workbench once |
| Channels | Created only when opened; displayed histograms use a bounded 256-pixel preview, not full-resolution rasterization |
| Thumbnails | Render existing content with an ignored presentation transform; no tile-dictionary snapshots or COW ownership changes |

Unchanged numeric values do not overwrite focused, uncommitted text. Rebinding to a different target explicitly resets pending numeric text. Inspector height allocation changes only when its requested/allocated size changes.

## Model notifications and recovery

`EditorSession.Notify()` remains conservative and treats unspecified updates as document changes. `EditorChangedEventArgs` identifies `Document`, `ActiveTarget` and `SavedState` changes without breaking existing `Changed` subscribers. Selecting a different layer or mask advances the general session `Revision` (so stale asynchronous work is still rejected), but not `ContentRevision`. Selecting the same target is a no-op.

Commits, undo, redo, cancellation and unqualified notifications advance `ContentRevision`. Recovery uses this content revision and only serializes dirty, edited documents. Selection and saved-state notifications do not trigger complete ZIP recompression. Recovery still runs on the existing host thread; large edited archives can still consume CPU during serialization. No worker-thread or constant-time compression claim is made.

A Move-tool click selects immediately. A geometry-only gesture description is retained until movement reaches one logical pixel; only then does a real transform open its COW transaction. A plain click no longer creates an empty Move history entry. Real drags retain their complete undo/cancel behavior.

## Deferred preview rendering

Tone controls are shown before their sampled histogram is requested. A single 50 ms UI timer coalesces optional histogram requests, reads only the latest bound target, and waits for an active gesture to complete. Unloaded inspectors and inactive Channels panels do no histogram work. This is deferred UI work, not a background GPU thread.

`DocumentPreviewCache` owns a separate renderer. Its single bounded entry checks canvas identity/dimensions, prefix length, font revision and `LayerRenderStamp` values. Stamps capture every current render dependency, including in-place pixel/mask revisions, transforms, alpha, blend, text, shape and adjustment settings. Names, locks, selection and editing channel are excluded. A tone layer's own settings and layers above it are outside its input prefix, so editing those does not rerasterize its input histogram.

Returned previews are **borrowed read-only by convention** and must not be mutated by consumers. The cache does not change source tile ownership. Its renderer never prunes or overwrites viewport filter caches. A font is copied only when the source font revision changes. Cached previews are at most 512 pixels on their longest edge (the inspectors request 192/256); full-resolution export and Load Alpha as Selection remain full resolution.

## Scene and overlay separation

The viewport has two retained `SKCanvasElement` drawings. The scene contains checkerboard, document composite and mask inspection. The interaction layer contains handles, brush cursor, selection boundaries, crop/gradient previews and rulers.

`Invalidate()` still invalidates both for image changes, painting, transforms, zoom/pan and explicit preview operations. `InvalidateOverlay()` is used for hover, selection target changes, tool changes and marching ants. A saved-state notification does not invalidate the viewport. This avoids unnecessary compositor command recording; it is not a claim that the operating system never recomposites the window or that overlays consume no GPU time.

## Validation and diagnostics

The existing headless suites remain in Build/Release validation. Additional cases cover selection no-ops, saved/content revisions, conservative notifications, preview dependency changes, prefix reuse, cache isolation, dimension/font invalidation, and thumbnail COW ownership.

`tests/browser/ui-responsiveness.spec.mjs` uses trusted pointer/keyboard events and the real Uno visual tree. It checks warmed selection across pixel/text/shape layers; canvas clicks without edits; stable template/control/row/inspector/tab counts; hover/ants without scene redraw; real painting invalidation; numeric rebinding after undo; and cached tone/channel previews. No JavaScript command endpoint mutates the model.

Opt-in `?test=1` diagnostics expose `lastSelectionRefreshMs`, `selectionRefreshes`, `buttonsCreated`, `buttonTemplateBuilds`, `layerRowsCreated`, `inspectorBuilds`, `optionsBuilds`, `tabsCreated`, `historyButtonsCreated`, `toneHistogramBuilds`, `channelHistogramBuilds`, `sceneRenders` and `overlayRenders`. Cumulative counts describe actual code paths; they are not GPU times. `ui-selection-performance.json` is included in the browser-validation artifact under `browser-tests/`.

Timing samples cover synchronous C# workbench refresh after pointer input, not browser dispatch, complete layout/presentation, input-to-photon latency or a universal hardware speedup. Correctness tests prefer structural invariants over machine-dependent millisecond thresholds. Cold schema construction and native text/layout costs still exist.
