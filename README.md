<div align="center">

# ImageSpace

### A local-first image studio. Built in C#. At home on desktop and the web.

[![Build](https://github.com/wieslawsoltes/ImageSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ImageSpace/actions/workflows/build.yml)
[![Desktop](https://github.com/wieslawsoltes/ImageSpace/actions/workflows/desktop.yml/badge.svg)](https://github.com/wieslawsoltes/ImageSpace/actions/workflows/desktop.yml)
[![Pages](https://github.com/wieslawsoltes/ImageSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/ImageSpace/actions/workflows/pages.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

[**Open ImageSpace**](https://wieslawsoltes.github.io/ImageSpace/) · [User guide](docs/user-guide.md) · [Architecture](docs/architecture.md) · [Feature matrix](docs/features.md) · [Build and release](docs/development.md)

</div>

---

ImageSpace is an independent layered image editor built with **Uno Platform**, **SkiaSharp**, and an optional **WebGPU compute backend**. It brings a familiar professional photo-editing workspace to a genuine Uno WebAssembly application and shared Windows, Linux, and macOS hosts.

The project is organized as **eleven reusable, packable .NET libraries** rather than one application-sized rendering control. Pixels, selections, brushes, filters, document transactions, file formats, rendering, custom controls, the viewport, storage contracts, and the workbench can be used independently.

> **Status: 0.1.0-alpha.1 — functional initial release, not full Photoshop parity.** The interface follows Photoshop workspace conventions but is not pixel-identical. PSD support is a deliberately bounded raster interchange implementation, not a lossless Photoshop editor. [Read the exact supported boundary](docs/features.md) before editing important files.

## The workspace

A compact menu and tool-options bar, a vertical custom-drawn toolbox, tabbed documents, rulers, on-canvas transform handles, a color spectrum, numeric properties, layers, channels/histograms, and history. The included **After the light** artwork is generated locally and remains editable as separate pixel, shape, and type layers. No Adobe artwork, icons, fonts, or network image assets are used.

![ImageSpace workspace, captured from the real Uno browser application](https://wieslawsoltes.github.io/ImageSpace/screenshots/workspace.png)

## What works

| Area | Implemented behavior |
| --- | --- |
| Painting | Pressure-aware round brush, pencil, eraser, clone stamp, basic smudge, dodge and burn; size, hardness, opacity, flow and spacing kernels |
| Selection | Rectangle, ellipse, lasso, contiguous color, add/subtract/intersect, invert, feather and alpha selection |
| Layers | Sparse RGBA pixel layers, editable multiline type, rectangle/ellipse shapes, visibility, locks, opacity, sixteen blend modes, masks, duplicate/reorder, rasterize and merge |
| Geometry | Move, eight resize handles, rotation, constrained transforms, numeric properties, non-destructive crop, canvas/image sizing and rotation |
| Filters | Fifteen CPU kernels; eight browser WebGPU color kernels; six non-destructive adjustment types composed through Skia |
| Documents | Multiple tabs, tile-sharing undo/redo, native `.imagespace` ZIP archives, bounded RGB/8 PSD interchange, image import and PNG/JPEG/WebP export |
| Recovery | IndexedDB in the browser and atomic recovery-file replacement on desktop; visible failures instead of silent data loss |
| Validation | Headless engine and raster regressions, real pointer/keyboard browser tests, rendered-pixel assertions, file roundtrips and explicit GPU/fallback diagnostics |

The application contains its own user guide and compatibility reference under **Help**. Every advertised menu command has an implementation or an explicit supported-format constraint; unsupported Photoshop features are not represented as working AI or cloud services.

## Rendering, without proprietary dependencies

The shared compositor draws revision-cached image tiles, text, vector shapes, blend layers and masks into Uno's `SKCanvasElement`. Uno selects the host graphics backend. Skia supplies GPU-capable rendering and live color/image filters, while software rendering remains a supported host fallback.

The browser additionally ships an independently reusable WebGPU module for invert, grayscale, sepia, brightness/contrast, saturation, gamma, threshold and posterize. Unsupported kernels or unavailable adapters return to the CPU implementation. GPU output is read back into the same editable tile model, preserving deterministic undo and native saves. This is **not a GPU-only engine**, and a CI software adapter is not a physical-GPU performance benchmark.

The build pins **.NET SDK 10.0.401**, **Uno SDK 6.7.30**, and the **SkiaSharp 3.119.2 managed/native ABI**. Skia is intentionally kept on the version compatible with this Uno renderer rather than independently upgraded to a mismatched native ABI. See [dependency and backend policy](docs/architecture.md#dependency-policy).

## Reusable libraries

| Package | Responsibility | Uno dependency |
| --- | --- | --- |
| `ImageSpace.Core` | Colors, sparse copy-on-write tiles, layers and document invariants | No |
| `ImageSpace.Imaging` | Brush engine, selections, raster geometry, histogram and procedural sample | No |
| `ImageSpace.Filters` | Deterministic color and convolution kernels | No |
| `ImageSpace.Editing` | Transactions, bounded history and document commands | No |
| `ImageSpace.Documents` | Native archives and bounded PSD codec | No |
| `ImageSpace.Skia` | Skia compositor, tile upload cache and image codecs | No |
| `ImageSpace.WebGpu` | Compute contracts and standalone browser shader module | No |
| `ImageSpace.Storage` | Host-independent file and recovery contracts | No |
| `ImageSpace.Controls` | Custom vector icons, buttons, menus, numeric fields, spectrum and histogram | Yes |
| `ImageSpace.Editor` | Embeddable image viewport and pointer interaction | Yes |
| `ImageSpace.Workbench` | Editor shell, panels, commands and multi-document workflows | Yes |

NuGet packages are produced as CI artifacts. Public NuGet/npm publication requires the corresponding release credentials; a successful build does not mean packages have already been published to a registry. The standalone WebGPU library is also packed as `@wieslawsoltes/imagespace-webgpu`.

### Embed the editor

Reference `ImageSpace.Editor` from source, or install its CI-produced package from a local NuGet feed:

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

The full shell is `new StudioWorkbench(session, storage)`, where `storage` implements `IEditorStorage`. No global service locator or application singleton is required by the engine.

## Build and run

Install .NET 10 and the browser workload, then fetch the OFL-licensed font assets:

```sh
dotnet workload install wasm-tools --skip-manifest-update
python3 scripts/fetch-assets.py

# Desktop host on the current operating system.
dotnet run --project src/ImageSpace.App -f net10.0-desktop -p:ImageSpaceDesktopOnly=true

# Release browser build, with the same prefix used by GitHub Pages.
dotnet publish src/ImageSpace.App -c Release -f net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/ImageSpace/ -o artifacts/publish
python3 scripts/collect-site.py artifacts/publish site
python3 scripts/serve-site.py --root site --port 4173
```

Open `http://127.0.0.1:4173/ImageSpace/`. Use HTTPS for remote browser hosting; localhost is a secure-context exception for WebGPU. Native graphics libraries and display-server requirements are documented in [development.md](docs/development.md).

### Run validation

```sh
dotnet run --project tests/ImageSpace.Tests -c Release
dotnet run --project tests/ImageSpace.RegressionTests -c Release
npm ci
npx playwright install chromium
npm run test:browser
```

Browser tests require a collected `site/` build. They drive real Uno controls through coordinates obtained from an **opt-in, read-only** diagnostics surface; they do not mutate the document through JavaScript test hooks. The Pages workflow repeats the acceptance suite against the public URL and verifies the deployed source SHA.

## File safety and compatibility

Use **`.imagespace` for editable roundtrips**. Keep original imported files. PSD import is limited to PSD v1, RGB, eight bits per channel, with raw or PackBits pixel data. Unsupported color modes, large-document PSB, ZIP prediction and malformed bounds are rejected. Unsupported Photoshop metadata is not preserved. PSD exports rasterize type, shape transforms and masks; documents with live adjustment layers export a flattened compatibility image.

Working limits are 8192 pixels per dimension, 16 megapixels per surface, 128 layers per document and 12 open documents. These are safety limits, not guarantees that every combination fits the memory budget of a browser tab. Recovery is local to one browser/device and is not a backup service.

## License and attribution

ImageSpace source is **MIT licensed**. Uno Platform is Apache-2.0, SkiaSharp is MIT, Skia is BSD-3-Clause, and Inter is distributed under the SIL Open Font License. Dependency notices and source references are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

ImageSpace is not affiliated with, endorsed by, or a product of Adobe. Adobe Photoshop is a trademark of Adobe. No Adobe source code, SDK, brand iconography, sample files or cloud endpoints are included.
