<div align="center">

# ImageSpace

### A local-first image studio. Built in C#. At home on desktop and the web.

[![Build](https://github.com/wieslawsoltes/ImageSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ImageSpace/actions/workflows/build.yml)
[![Desktop](https://github.com/wieslawsoltes/ImageSpace/actions/workflows/desktop.yml/badge.svg)](https://github.com/wieslawsoltes/ImageSpace/actions/workflows/desktop.yml)
[![Pages](https://github.com/wieslawsoltes/ImageSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/ImageSpace/actions/workflows/pages.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

[**Open ImageSpace**](https://wieslawsoltes.github.io/ImageSpace/) · [User guide](docs/user-guide.md) · [Curves & Levels](docs/tonal-adjustments.md) · [Architecture](docs/architecture.md) · [Feature matrix](docs/features.md) · [Development](docs/development.md)

</div>

---

ImageSpace is an independent layered image editor built with **Uno Platform**, **SkiaSharp**, and an optional **WebGPU compute backend**. Its professional photo-editing workspace runs as a genuine Uno WebAssembly application and shares its C# editor with Windows, Linux and macOS hosts.

Eleven reusable .NET libraries separate the document model, copy-on-write tiles, brushes, selections, filters, transactions, file formats, rendering, controls, viewport, storage contracts and workbench. The application is not a single monolithic canvas or an HTML screenshot of a desktop editor.

> **0.2.0-alpha.1 — expanded compatibility and performance release, not full Photoshop parity.** The workspace follows familiar Photoshop conventions but is not pixel-identical. PSD support is bounded raster interchange, not lossless Photoshop editing. Read the [supported boundary](docs/features.md) before working with important originals.

![Real Uno browser workspace](https://wieslawsoltes.github.io/ImageSpace/screenshots/workspace.png)

## Editing capabilities

| Area | Implemented behavior |
| --- | --- |
| Paint | Pressure-aware brush, pencil, eraser, clone, basic smudge/dodge/burn; size, hardness, opacity and flow |
| Selection | Rectangle, ellipse, lasso, contiguous color, add/subtract/intersect, invert, feather, alpha selection, expand/contract/border/smooth |
| Layers | Sparse pixels, editable multiline type, rectangle/ellipse shapes, masks, sixteen blend modes, opacity, visibility, locks, duplicate/reorder and rasterization |
| Geometry | Eight-handle scaling, rotation, constrained motion, numeric properties, non-destructive crop and canvas/image sizing |
| Tone | **Curves and Levels**, composite RGB and separate color channels, draggable graphs, numeric/keyboard editing, presets and live undoable previews |
| Effects | Fourteen CPU filters, eight optional WebGPU color kernels and eight live Skia adjustment types |
| Files | Editable native archives, bounded RGB/8 PSD, raster import, PNG/JPEG/WebP export, multiple document tabs |
| Recovery | Local IndexedDB or atomic desktop recovery file; visible error reporting |

The **After the light** sample is original procedural artwork with separate pixel, type and shape layers. No Adobe artwork, icons, fonts or remote stock images are included. The Help menu contains the user guide, compatibility boundary and rendering diagnostics.

## Non-destructive Curves and Levels

Create an adjustment from **Image** or **Adjustments**. It is inserted above the selected layer. Dragging updates the actual image before pointer release; the complete gesture produces one history state. Escape cancels without retaining the preview. Numeric inputs, keyboard nudging, channel switching and presets work with the same immutable settings and transaction model.

Curves uses independently implemented shape-preserving cubic interpolation with 2–16 points per channel. Levels exposes input black/white, gamma and output endpoints. Alpha is preserved, source pixels are untouched, and cached Skia lookup filters avoid rebuilding unchanged state. Inspector histograms are explicitly sampled. Read the [algorithm, reuse API and file compatibility](docs/tonal-adjustments.md).

## Rendering and dependencies

The shared compositor draws revision-cached tiles, text, shapes, blends, masks and live adjustments into Uno's `SKCanvasElement`. The host selects its available Skia graphics backend; software rendering remains possible. Curves/Levels use shared Skia lookup filters, rather than a WebGPU roundtrip for every pointer movement.

An independently reusable browser WebGPU module accelerates invert, grayscale, sepia, brightness/contrast, saturation, gamma, threshold and posterize. Output returns to the authoritative tile model for undo and native saving. Unsupported kernels/adapters use the CPU path. This is **not a GPU-only engine**, and software-adapter CI does not establish physical-GPU performance.

The compatible baseline is pinned to **.NET SDK 10.0.401**, **Uno SDK 6.7.30** and **SkiaSharp 3.119.4**. Managed/native Skia ABI compatibility is intentional; independently changing only one side is not a supported upgrade.

## Adjustment-mask editing

Pixel, type, shape and adjustment masks share the same editing target pipeline. The mask inspector provides density, feather, inversion, enable/disable and view-only grayscale/red-overlay inspection. Painting and filters modify authored coverage, never the underlying layer pixels. Density/feather remain independently editable. Masks currently share their layer transform; groups and independent mask transforms are not yet supported.

[Mask editing architecture and verification](docs/mask-editing.md) covers alpha compositing, archive version 3, regression coverage and the exact supported boundary.

## Compatibility and measured CPU performance

The 0.2 alpha adds section-bounded PSD decoding, compressed export, Unicode names, resolution and user-mask import; selection morphology; tile/row raster operations; and streaming native archives. [Implementation and compatibility details](docs/compatibility-performance.md) describe the exact contracts and limits.

A reproducible same-process Release benchmark compares the frozen pre-optimization implementation against the new paths. It reports median timings and allocations rather than claiming hardware GPU acceleration. Run `dotnet run --project tests/ImageSpace.Benchmarks -c Release`; CI retains `performance-results.json`.

## Reusable libraries

| Package | Responsibility |
| --- | --- |
| `ImageSpace.Core` | Colors, tiles, layers, document invariants, immutable tone curves/levels and lookup math |
| `ImageSpace.Imaging` | Brushes, selections, raster geometry, histograms and original sample |
| `ImageSpace.Filters` | Color/convolution filters and sparse tone-lookup processing |
| `ImageSpace.Editing` | Transactions, history, rollback and editor commands |
| `ImageSpace.Documents` | Versioned native archives and bounded PSD codec |
| `ImageSpace.Skia` | Compositor, revision caches, live adjustments and image codecs |
| `ImageSpace.WebGpu` | Compute contracts and standalone JavaScript/WGSL kernels |
| `ImageSpace.Storage` | Host-independent file/recovery contracts |
| `ImageSpace.Controls` | Custom icons, buttons, menus, numeric fields, spectrum, histograms, curve and levels editors |
| `ImageSpace.Editor` | Embeddable viewport, camera and pointer gestures |
| `ImageSpace.Workbench` | Workspace, panels, tonal inspector, commands and multi-document workflows |

The first eight packages have no Uno dependency. Controls, Editor and Workbench target both browser and desktop. CI produces all eleven NuGet packages plus the standalone `@wieslawsoltes/imagespace-webgpu` tarball. **Registry publication is separate** and requires release credentials; package artifacts do not imply NuGet.org/npm publication.

### Embed the viewport

Reference the source project or a CI-produced package in a local NuGet feed:

```csharp
using ImageSpace.Core;
using ImageSpace.Editing;
using ImageSpace.Editor;

var session = new EditorSession(new ImageDocument(1600, 1000, "Untitled"));
session.AddLayer("Paint");
window.Content = new ImageViewport(session)
{
    Tool = EditorTool.Brush,
    Foreground = new Rgba32(54, 145, 230)
};
```

Embed the complete workspace with `new StudioWorkbench(session, storage)`, where `storage` implements `IEditorStorage`. Pure tone math and controls can be reused separately; see the [Curves/Levels examples](docs/tonal-adjustments.md#reuse-from-c).

## Build and validate

Install the pinned .NET SDK, Python 3 and Node 22:

```sh
dotnet workload install wasm-tools --skip-manifest-update
python3 scripts/fetch-assets.py

# Native desktop host.
dotnet run --project src/ImageSpace.App -f net10.0-desktop -p:ImageSpaceDesktopOnly=true

# Genuine Uno browser application, with the GitHub Pages project prefix.
dotnet publish src/ImageSpace.App -c Release -f net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/ImageSpace/ -o artifacts/publish
python3 scripts/collect-site.py artifacts/publish site
python3 scripts/serve-site.py --root site --port 4173
```

Open `http://127.0.0.1:4173/ImageSpace/`. Use HTTPS for remote hosting. Initial restore/font acquisition needs network access; documents are not uploaded by the application.

```sh
dotnet run --project tests/ImageSpace.Tests -c Release
dotnet run --project tests/ImageSpace.RegressionTests -c Release
npm ci
npx playwright install chromium
npm run test:browser
```

Browser tests require the collected `site/` tree. They use real pointer, keyboard, file-chooser and download events, with opt-in read-only control geometry—not JavaScript mutation hooks. Pixel assertions test visible painting, undo and live tone previews. Generated browser scripts are syntax-checked before publication. The Pages workflow verifies source identity and reruns acceptance on the deployed URL.

Build, desktop, Pages, tag-release and formatting workflows are under `.github/workflows`. Native distributions are unsigned developer builds; production signing/notarization is not implied. See [development](docs/development.md).

## Files and limits

**Use `.imagespace` for editable roundtrips and retain original imports.** Native manifest version 2 retains Curves/Levels; the reader remains compatible with version 1. An older version-1-only application must reject the newer tone document instead of silently changing its appearance.

PSD import supports version-1 RGB/8 raw, PackBits, ZIP and row-predicted ZIP channels, Unicode layer names, resolution, and placed user masks with density/feather metadata. Smart objects, editable Photoshop type/adjustment metadata, groups, clipping chains and ICC profiles remain outside the interchange boundary. PSD exports rasterize type/shapes/transforms and supported masks; visible live adjustments use a flattened compatibility image. PNG/JPEG/WebP exports contain the composite, not edit state.

Working limits: 8192 pixels per side, 16 megapixels per surface, 128 layers and twelve documents. These are safety limits, not guarantees that all combinations fit a browser tab. Local recovery covers the active document and is not a cloud backup.

## License

ImageSpace source and original artwork are **MIT licensed**. Uno Platform is Apache-2.0; SkiaSharp is MIT; Skia is BSD-3-Clause; Inter is SIL Open Font License. See [third-party notices](THIRD-PARTY-NOTICES.md).

ImageSpace is independent and not affiliated with Adobe. Adobe Photoshop is an Adobe trademark. No Adobe source, private SDK, brand assets, sample files or cloud services are included.
