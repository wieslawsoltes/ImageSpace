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

ImageSpace is an independent layered image editor built with **Uno Platform**, **SkiaSharp**, and an optional **WebGPU compute backend**. Its photo-editing workspace runs as a genuine Uno WebAssembly application and shares its C# editor with Windows, Linux and macOS hosts.

Eleven reusable .NET libraries separate the document model, copy-on-write tiles, brushes, selections, filters, transactions, file formats, rendering, controls, viewport, storage contracts and workbench. The application is not a monolithic canvas or an HTML screenshot of a desktop editor.

> **0.3.2-alpha.1 — compatibility and performance work, not full Photoshop parity.** The workspace follows familiar Photoshop conventions but is not pixel-identical. PSD support is bounded raster interchange, not lossless Photoshop editing. Read the [supported boundary](docs/features.md) before working with important originals.

![Real Uno browser workspace](https://wieslawsoltes.github.io/ImageSpace/screenshots/workspace.png)

## New in 0.3.2

**Selection updates values, not the entire workspace.** Inspector schemas, layer rows, toolbars, tabs and visible history controls are retained. Button templates are parsed once per UI thread. Optional histograms are deferred and sampled through independent caches; selecting a layer does not compress a recovery archive. Cursor/handle/selection overlays have a separate retained drawing from image compositing. Real-pointer browser tests assert stable control counts, correct undo rebinding and image-cache reuse.

[UI responsiveness: implementation, diagnostics and validation](docs/ui-responsiveness.md).

## Mask application and compositing

**Apply a mask without losing the layer's source geometry.** Layer → Apply layer mask bakes effective density, feather and affine coverage into authored pixel alpha, preserves RGB and off-canvas source extent, and records one undoable edit. Disabled masks require an explicit enable; non-raster and adjustment layers are not silently flattened.

**Fewer redundant compositing surfaces.** Normal layers with unit opacity and no effective mask use a direct draw path. Masks, other blends, fractional opacity and document isolation retain their original compositing semantics. A dedicated regression suite compares the direct and isolated paths, checks mask-application ownership and roundtrips, and emits reproducible raster CPU measurements. Timing results are not physical-GPU or whole-application speed claims.

[Mask application, renderer contracts and precision limits](docs/mask-application-and-rendering.md).

## Independent masks and exact contours

Link/unlink masks without a jump, manipulate their affine frame with on-canvas handles or numeric inputs, and retain the result through undo and native saves. Prepared pixel mappings avoid repeated trigonometry in coverage loops. Placement-only changes reuse the recorded mask source, while selection outlines stream exact merged boundary segments with width-bounded scratch storage instead of allocating a full image.

[Mask placement and interaction performance](docs/mask-placement.md).

## Editing capabilities

| Area | Implemented behavior |
| --- | --- |
| Paint | Pressure-aware brush, pencil, eraser, clone, basic smudge/dodge/burn; size, hardness, opacity and flow |
| Selection | Rectangle, ellipse, lasso, contiguous color, add/subtract/intersect, invert, feather, alpha selection, expand/contract/border/smooth |
| Layers | Sparse pixels, editable multiline type, rectangle/ellipse shapes, masks and raster-mask application, sixteen blends, opacity, visibility, locks, duplicate/reorder and rasterization |
| Geometry | Eight-handle scaling, rotation, constrained motion, numeric properties, non-destructive crop and canvas/image sizing |
| Tone | **Curves and Levels**, composite RGB and separate color channels, draggable graphs, numeric/keyboard editing, presets and live undoable previews |
| Effects | Fourteen CPU filters, eight optional WebGPU color kernels and eight live Skia adjustment types |
| Files | Editable native archives, bounded RGB/8 PSD, raster import, PNG/JPEG/WebP export, multiple document tabs |
| Recovery | Local IndexedDB or atomic desktop recovery file; visible error reporting |

The **After the light** sample is original procedural artwork with separate pixel, type and shape layers. No Adobe artwork, icons, fonts or remote stock images are included. Help contains the user guide, compatibility boundary, mask-application guidance and rendering diagnostics.

## Non-destructive Curves and Levels

Create an adjustment from **Image** or **Adjustments**. It is inserted above the selected layer. Dragging updates the actual image before pointer release; the complete gesture produces one history state. Escape cancels without retaining the preview. Numeric inputs, keyboard nudging, channel switching and presets use the same immutable settings and transaction model.

Curves uses independently implemented shape-preserving cubic interpolation with 2–16 points per channel. Levels exposes input black/white, gamma and output endpoints. Alpha is preserved, source pixels are untouched, and cached Skia lookup filters avoid rebuilding unchanged state. Inspector histograms are explicitly sampled. Read the [algorithm, reuse API and file compatibility](docs/tonal-adjustments.md).

## Rendering and dependencies

The shared compositor draws revision-cached tiles, text, shapes, blends, masks and live adjustments into Uno's `SKCanvasElement`. The host selects its available Skia graphics backend; software rendering remains possible. Curves/Levels use shared Skia lookup filters, rather than a WebGPU roundtrip for every pointer movement.

An independently reusable browser WebGPU module accelerates invert, grayscale, sepia, brightness/contrast, saturation, gamma, threshold and posterize. Output returns to the authoritative tile model for undo and native saving. Unsupported kernels/adapters use the CPU path. This is **not a GPU-only engine**, and software-adapter CI does not establish physical-GPU performance.

The compatible baseline is pinned to **.NET SDK 10.0.401**, **Uno SDK 6.7.30** and **SkiaSharp 3.119.4**. Managed/native Skia ABI compatibility is intentional; independently changing only one side is not a supported upgrade.

## Mask editing

Pixel, type, shape and adjustment masks share the same editing target pipeline. The mask inspector provides density, feather, inversion, enable/disable and view-only grayscale/red-overlay inspection. Painting and filters modify authored coverage, never underlying layer pixels. Density/feather remain independently editable. Masks are linked by default. Unlink them to move, rotate and resize coverage independently, then relink without a visual jump. Viewport handles and numeric controls share affine geometry; placement edits do not resample authored mask pixels. Group masks remain unsupported.

[Mask editing](docs/mask-editing.md) describes coverage tools. [Mask placement](docs/mask-placement.md) documents coordinate frames, archive v4 and source caching. [Applying a mask](docs/mask-application-and-rendering.md) is the separate destructive workflow: coverage is sampled at source resolution, and later zoom-dependent edge samples need not be identical to live filtering.

## Compatibility and reproducible CPU measurements

Bounded PSD decoding includes compressed channels/export, Unicode names, resolution and user-mask import. Selection morphology, tile/row operations and streaming archives avoid several full-image intermediates. [Compatibility/performance details](docs/compatibility-performance.md) describe these contracts and limits.

`ImageSpace.Benchmarks` compares frozen pre-optimization algorithms with newer paths. `ImageSpace.LayerTests` compares warmed direct versus isolated raster compositing. Both record median timings, sample data and allocation scope without asserting machine-dependent speed thresholds. CI retains `performance-results.json` and `layer-performance-results.json`.

## Download

Every [release](https://github.com/wieslawsoltes/ImageSpace/releases/latest) ships a self-contained, single-file desktop app — no .NET install needed:

| OS | x64 | Arm64 |
| --- | --- | --- |
| Windows | `ImageSpace-<version>-win-x64.zip` | `ImageSpace-<version>-win-arm64.zip` |
| macOS | `ImageSpace-<version>-osx-x64.tar.gz` | `ImageSpace-<version>-osx-arm64.tar.gz` |
| Linux | `ImageSpace-<version>-linux-x64.tar.gz` | `ImageSpace-<version>-linux-arm64.tar.gz` |

Extract and run `ImageSpace` (`ImageSpace.exe` on Windows). Builds are not code-signed yet: on macOS clear the quarantine flag with `xattr -d com.apple.quarantine ImageSpace`; on Windows choose **More info → Run anyway** in SmartScreen. Verify downloads against `SHA256SUMS`.

The libraries below are published to [NuGet.org](https://www.nuget.org/packages?q=ImageSpace), e.g. `dotnet add package ImageSpace.Core --prerelease`.

## Reusable libraries

| Package | Responsibility |
| --- | --- |
| `ImageSpace.Core` | Colors, tiles, layers, document invariants, immutable tone curves/levels and lookup math |
| `ImageSpace.Imaging` | Brushes, selections, raster geometry, histograms and original sample |
| `ImageSpace.Filters` | Color/convolution filters and sparse tone-lookup processing |
| `ImageSpace.Editing` | Transactions, history, rollback, validated mask application and editor commands |
| `ImageSpace.Documents` | Versioned native archives and bounded PSD codec |
| `ImageSpace.Skia` | Compositor, revision caches, live adjustments, mask baking and image codecs |
| `ImageSpace.WebGpu` | Compute contracts and standalone JavaScript/WGSL kernels |
| `ImageSpace.Storage` | Host-independent file/recovery contracts |
| `ImageSpace.Controls` | Custom icons, buttons, menus, numeric fields, spectrum, histograms, curve and levels editors |
| `ImageSpace.Editor` | Embeddable viewport, camera and pointer gestures |
| `ImageSpace.Workbench` | Workspace, panels, tonal inspector, commands and multi-document workflows |

The first eight packages have no Uno dependency. Controls, Editor and Workbench target browser and desktop. CI produces all eleven NuGet packages and the standalone `@wieslawsoltes/imagespace-webgpu` tarball. Tagged releases publish the NuGet packages to NuGet.org; npm publication of the WebGPU tarball remains separate and requires its own credentials.

### Embed the viewport

Reference the NuGet.org packages, the source projects or a CI-produced package in a local NuGet feed:

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

Embed the complete workspace with `new StudioWorkbench(session, storage)`, where `storage` implements `IEditorStorage`. Pure tone math and controls can be reused separately; see the [Curves/Levels examples](docs/tonal-adjustments.md#reuse-from-c). Mask-application and renderer ownership examples are in the [rendering guide](docs/mask-application-and-rendering.md).

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
dotnet run --project tests/ImageSpace.MaskTests -c Release
dotnet run --project tests/ImageSpace.MaskEditingTests -c Release
dotnet run --project tests/ImageSpace.CompatibilityTests -c Release
dotnet run --project tests/ImageSpace.LayerTests -c Release
dotnet run --project tests/ImageSpace.Benchmarks -c Release
npm ci
npx playwright install chromium
npm run test:browser
```

Browser tests require the collected `site/` tree. They use real pointer, keyboard, file-chooser and download events, with opt-in read-only control geometry—not JavaScript mutation hooks. Pixel assertions cover painting, undo, tonal previews and mask application. Generated scripts are syntax-checked before publication. Pages verifies source identity and reruns acceptance on the deployed URL.

Build, desktop, Pages, release and formatting workflows are under `.github/workflows`. **Release** runs for `v*` tags or a supplied manual version: it runs the validation suites and browser acceptance, publishes self-contained single-file desktop executables for Windows, macOS and Linux (x64 and arm64), packs the eleven libraries with symbols and the WebGPU tarball, and emits `SHA256SUMS`. Tags attach all assets to a GitHub Release and publish the NuGet packages to NuGet.org with [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) (OIDC, no stored API key) from the protected `nuget` environment. Manual runs are dry runs that build and upload every asset as workflow artifacts but publish nothing. Native distributions are unsigned developer builds; production signing/notarization is not implied. See [development](docs/development.md).

## Files and limits

**Use `.imagespace` for editable roundtrips and retain original imports.** The native reader accepts versions 1–4. Tone settings require version 2; adjustment masks, density/feather and fractional adjustment crossfades require version 3; independent mask placement requires version 4. The writer selects the minimal required version. Applying a raster mask leaves ordinary pixel data and does not itself require a new version. Older readers must reject unsupported versions instead of silently changing appearance.

PSD import supports version-1 RGB/8 raw, PackBits, ZIP and row-predicted ZIP channels, Unicode names, resolution and placed user masks with supported density/feather metadata. Smart objects, editable Photoshop type/adjustments, groups, clipping chains and ICC profiles remain outside the interchange boundary. PSD export rasterizes type/shapes/transforms and supported masks; visible live adjustments use a flattened compatibility image. PNG/JPEG/WebP exports contain the composite, not edit state.

Working limits: 8192 pixels per side, 16 megapixels per surface, 128 layers and twelve documents. These are safety limits, not guarantees that all combinations fit a browser tab. Local recovery covers the active document and is not a cloud backup.

## License

ImageSpace source and original artwork are **MIT licensed**. Uno Platform is Apache-2.0; SkiaSharp is MIT; Skia is BSD-3-Clause; Inter is SIL Open Font License. See [third-party notices](THIRD-PARTY-NOTICES.md).

ImageSpace is independent and not affiliated with Adobe. Adobe Photoshop is an Adobe trademark. No Adobe source, private SDK, brand assets, sample files or cloud services are included.
