<div align="center">

# ImageSpace

### A local-first image studio. Built in C#. At home on desktop and the web.

[![Build](https://github.com/wieslawsoltes/ImageSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ImageSpace/actions/workflows/build.yml)
[![Desktop](https://github.com/wieslawsoltes/ImageSpace/actions/workflows/desktop.yml/badge.svg)](https://github.com/wieslawsoltes/ImageSpace/actions/workflows/desktop.yml)
[![Pages](https://github.com/wieslawsoltes/ImageSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/ImageSpace/actions/workflows/pages.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/vpre/ImageSpace.Core.svg?label=NuGet)](https://www.nuget.org/packages/ImageSpace.Core)
[![Downloads](https://img.shields.io/nuget/dt/ImageSpace.Core.svg)](https://www.nuget.org/packages/ImageSpace.Core)

[**Open ImageSpace**](https://wieslawsoltes.github.io/ImageSpace/) · [User guide](docs/user-guide.md) · [Curves & Levels](docs/tonal-adjustments.md) · [Architecture](docs/architecture.md) · [Feature matrix](docs/features.md) · [Development](docs/development.md)

</div>

---

ImageSpace is an independent layered image editor built with **Uno Platform**, **SkiaSharp**, and an optional **WebGPU compute backend**. Its photo-editing workspace runs as a genuine Uno WebAssembly application and shares its C# editor with Windows, Linux and macOS hosts.

Eleven reusable .NET libraries separate the document model, copy-on-write tiles, brushes, selections, filters, transactions, file formats, rendering, controls, viewport, storage contracts and workbench. The application is not a monolithic canvas or an HTML screenshot of a desktop editor.

> **0.4.0-alpha.1 — compatibility and performance work, not full Photoshop parity.** The workspace follows familiar Photoshop conventions but is not pixel-identical. PSD support is bounded raster interchange, not lossless Photoshop editing. Read the [supported boundary](docs/features.md) before working with important originals.

![Real Uno browser workspace](https://wieslawsoltes.github.io/ImageSpace/screenshots/workspace.png)

## New in 0.4

**An ordered Filter Gallery backed by resident GPU sessions.** Preview, reorder, bypass and combine up to sixteen filters, compare with the original, then apply the complete recipe as one undoable edit. Cancel changes nothing; Repeat filter stack reuses the accepted settings. Preview images are sampled and coalesced without rebuilding the main retained selection UI.

**Thirteen browser GPU kernels.** Premultiplied Gaussian blur, sharpen, emboss, edges and block-reduced pixelation join the eight color kernels. Interactive sessions upload their source once and reuse ping-pong buffers, bind groups and pipelines. Intermediate stages stay on the GPU; only requested final outputs are read back. Seeded Noise and unavailable adapters retain CPU fallback. Desktop gallery filtering currently uses CPU kernels while document rendering remains Skia-based.

[Filter Gallery: workflow, reusable APIs, limits and validation](docs/filter-gallery.md) · [Transparent comparisons and acceptance](docs/filter-gallery-validation.md).

## Retained selection UI

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
| Effects | Fourteen CPU filters, thirteen optional WebGPU color/spatial kernels, an ordered Filter Gallery and eight live Skia adjustment types |
| Files | Editable native archives, bounded RGB/8 PSD, raster import, PNG/JPEG/WebP export, multiple document tabs |
| Recovery | Local IndexedDB or atomic desktop recovery file; visible error reporting |

The **After the light** sample is original procedural artwork with separate pixel, type and shape layers. No Adobe artwork, icons, fonts or remote stock images are included. Help contains the user guide, compatibility boundary, mask-application guidance and rendering diagnostics.

## Non-destructive Curves and Levels

Create an adjustment from **Image** or **Adjustments**. It is inserted above the selected layer. Dragging updates the actual image before pointer release; the complete gesture produces one history state. Escape cancels without retaining the preview. Numeric inputs, keyboard nudging, channel switching and presets use the same immutable settings and transaction model.

Curves uses independently implemented shape-preserving cubic interpolation with 2–16 points per channel. Levels exposes input black/white, gamma and output endpoints. Alpha is preserved, source pixels are untouched, and cached Skia lookup filters avoid rebuilding unchanged state. Inspector histograms are explicitly sampled. Read the [algorithm, reuse API and file compatibility](docs/tonal-adjustments.md).

## Rendering and dependencies

The shared compositor draws revision-cached tiles, text, shapes, blends, masks and live adjustments into Uno's `SKCanvasElement`. The host selects its available Skia graphics backend; software rendering remains possible. Curves/Levels use shared Skia lookup filters, rather than a WebGPU roundtrip for every pointer movement.

An independently reusable browser WebGPU module accelerates thirteen color/spatial filters, including Gaussian blur, convolution and block-reduced pixelation. Its resident-session API reuses immutable source and intermediate buffers across ordered previews. Output returns to the authoritative tile model for undo and native saving. Unsupported kernels/adapters use the CPU path. This is **not a GPU-only engine**, and software-adapter CI does not establish physical-GPU performance.

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

## NuGet packages

All eleven libraries are MIT-licensed and published on [NuGet.org](https://www.nuget.org/packages?q=ImageSpace). The first eight (Core, Storage, Imaging, Filters, WebGpu, Editing, Documents, Skia) target `net10.0` and have no Uno dependency; only `ImageSpace.Skia` needs SkiaSharp. `ImageSpace.Controls`, `ImageSpace.Editor` and `ImageSpace.Workbench` target `net10.0-desktop` and `net10.0-browserwasm` on Uno Platform with the Skia renderer. All packages are versioned together (currently prereleases), and symbols ship on NuGet.org as `.snupkg` with SourceLink.

```sh
dotnet add package ImageSpace.Core --prerelease
```

| Package | Version | Downloads | Description |
| --- | --- | --- | --- |
| [ImageSpace.Core](https://www.nuget.org/packages/ImageSpace.Core) | [![NuGet](https://img.shields.io/nuget/vpre/ImageSpace.Core.svg)](https://www.nuget.org/packages/ImageSpace.Core) | [![Downloads](https://img.shields.io/nuget/dt/ImageSpace.Core.svg)](https://www.nuget.org/packages/ImageSpace.Core) | Copy-on-write raster tiles, layer document model, colors, masks and immutable Curves/Levels math |
| [ImageSpace.Storage](https://www.nuget.org/packages/ImageSpace.Storage) | [![NuGet](https://img.shields.io/nuget/vpre/ImageSpace.Storage.svg)](https://www.nuget.org/packages/ImageSpace.Storage) | [![Downloads](https://img.shields.io/nuget/dt/ImageSpace.Storage.svg)](https://www.nuget.org/packages/ImageSpace.Storage) | Host-independent file and recovery contracts for image editor applications |
| [ImageSpace.Imaging](https://www.nuget.org/packages/ImageSpace.Imaging) | [![NuGet](https://img.shields.io/nuget/vpre/ImageSpace.Imaging.svg)](https://www.nuget.org/packages/ImageSpace.Imaging) | [![Downloads](https://img.shields.io/nuget/dt/ImageSpace.Imaging.svg)](https://www.nuget.org/packages/ImageSpace.Imaging) | Pressure-aware brush engine, selections and morphology, fills, histograms and raster transforms |
| [ImageSpace.Filters](https://www.nuget.org/packages/ImageSpace.Filters) | [![NuGet](https://img.shields.io/nuget/vpre/ImageSpace.Filters.svg)](https://www.nuget.org/packages/ImageSpace.Filters) | [![Downloads](https://img.shields.io/nuget/dt/ImageSpace.Filters.svg)](https://www.nuget.org/packages/ImageSpace.Filters) | Deterministic color, convolution, blur and stylization kernels plus sparse tone-lookup processing |
| [ImageSpace.WebGpu](https://www.nuget.org/packages/ImageSpace.WebGpu) | [![NuGet](https://img.shields.io/nuget/vpre/ImageSpace.WebGpu.svg)](https://www.nuget.org/packages/ImageSpace.WebGpu) | [![Downloads](https://img.shields.io/nuget/dt/ImageSpace.WebGpu.svg)](https://www.nuget.org/packages/ImageSpace.WebGpu) | WebGPU RGBA compute kernels (JavaScript/WGSL) and host-independent capability contracts |
| [ImageSpace.Editing](https://www.nuget.org/packages/ImageSpace.Editing) | [![NuGet](https://img.shields.io/nuget/vpre/ImageSpace.Editing.svg)](https://www.nuget.org/packages/ImageSpace.Editing) | [![Downloads](https://img.shields.io/nuget/dt/ImageSpace.Editing.svg)](https://www.nuget.org/packages/ImageSpace.Editing) | Transactional editor session, bounded history, rollback, mask application and editing commands |
| [ImageSpace.Documents](https://www.nuget.org/packages/ImageSpace.Documents) | [![NuGet](https://img.shields.io/nuget/vpre/ImageSpace.Documents.svg)](https://www.nuget.org/packages/ImageSpace.Documents) | [![Downloads](https://img.shields.io/nuget/dt/ImageSpace.Documents.svg)](https://www.nuget.org/packages/ImageSpace.Documents) | Versioned native `.imagespace` archives and bounded 8-bit RGB PSD interchange |
| [ImageSpace.Skia](https://www.nuget.org/packages/ImageSpace.Skia) | [![NuGet](https://img.shields.io/nuget/vpre/ImageSpace.Skia.svg)](https://www.nuget.org/packages/ImageSpace.Skia) | [![Downloads](https://img.shields.io/nuget/dt/ImageSpace.Skia.svg)](https://www.nuget.org/packages/ImageSpace.Skia) | GPU-capable Skia compositor, revision-cached tiles, live adjustments, mask baking and image codecs |
| [ImageSpace.Controls](https://www.nuget.org/packages/ImageSpace.Controls) | [![NuGet](https://img.shields.io/nuget/vpre/ImageSpace.Controls.svg)](https://www.nuget.org/packages/ImageSpace.Controls) | [![Downloads](https://img.shields.io/nuget/dt/ImageSpace.Controls.svg)](https://www.nuget.org/packages/ImageSpace.Controls) | Dark editor buttons, icons, menus, numeric fields, color spectrum, histograms, curve and levels editors for Uno |
| [ImageSpace.Editor](https://www.nuget.org/packages/ImageSpace.Editor) | [![NuGet](https://img.shields.io/nuget/vpre/ImageSpace.Editor.svg)](https://www.nuget.org/packages/ImageSpace.Editor) | [![Downloads](https://img.shields.io/nuget/dt/ImageSpace.Editor.svg)](https://www.nuget.org/packages/ImageSpace.Editor) | Embeddable Uno image viewport with paint, selection, transform, crop and navigation gestures |
| [ImageSpace.Workbench](https://www.nuget.org/packages/ImageSpace.Workbench) | [![NuGet](https://img.shields.io/nuget/vpre/ImageSpace.Workbench.svg)](https://www.nuget.org/packages/ImageSpace.Workbench) | [![Downloads](https://img.shields.io/nuget/dt/ImageSpace.Workbench.svg)](https://www.nuget.org/packages/ImageSpace.Workbench) | Complete photo-editing workspace: panels, tonal inspector, menus, multi-document tabs and file commands |

Dependencies: `Core ← Imaging, Filters, Documents, Skia, Controls`; `Filters ← WebGpu`; `Imaging + Filters ← Editing`; `Editing + Skia + Controls ← Editor`; `Editor + Documents + Storage ← Workbench`. `Storage` has no dependencies. CI also produces the standalone npm tarball `@wieslawsoltes/imagespace-webgpu` (from `packages/webgpu`); it is not a NuGet package, and its npm publication is separate.

### ImageSpace.Core

The document model shared by every other package: `ImageDocument` with raster, text, shape and adjustment `Layer`s, sparse copy-on-write `PixelSurface` tiles (128×128, straight-alpha RGBA), masks with affine placement, and immutable Curves/Levels settings with lookup-table math. Use it standalone for headless image generation or processing. No dependencies and no UI.

```sh
dotnet add package ImageSpace.Core --prerelease
```

**Key types**
- `ImageDocument`: size, DPI, ordered `Layers`, `ActiveLayerId`, optional `Selection` coverage, `Snapshot()` and `Validate()`.
- `Layer`: kind, blend, opacity, transform, `Pixels`, `Mask` and mask placement, `Curves`/`Levels`; `Layer.Raster(...)` factory.
- `PixelSurface`: tiled pixels with `Get`/`Set`/`Fill`, row copies, `FromRgba`/`ToRgba` and per-tile revisions (limit 8192 px per side, 16 MP).
- `Rgba32`: 8-bit sRGB color with `Parse`, `Lerp` and `Over`.
- `ToneCurve`, `CurvesAdjustment`, `LevelsChannel`, `LevelsAdjustment`: immutable, validated tone settings.
- `RgbLookupTables`: `FromCurves`/`FromLevels` (with strength) and `Map(Rgba32)`.

**Usage**

```csharp
using ImageSpace.Core;

var document = new ImageDocument(1600, 1000, "Poster");
var paint = Layer.Raster("Paint", document.Width, document.Height);
paint.Pixels!.Fill(Rgba32.Parse("#1E2A3A"));
paint.Pixels.Set(10, 10, new Rgba32(255, 128, 0));
document.Layers.Add(paint);
document.ActiveLayerId = paint.Id;
document.Validate();

// Pure tone math: immutable settings become 8-bit lookup tables.
var curves = new CurvesAdjustment
{
    Rgb = new ToneCurve { Points = [new(0, 0), new(64, 38), new(192, 218), new(255, 255)] }
};
Rgba32 mapped = RgbLookupTables.FromCurves(curves).Map(paint.Pixels.Get(10, 10));
```

See the [Curves/Levels reuse guide](docs/tonal-adjustments.md#reuse-from-c) for interpolation and compatibility details.

### ImageSpace.Storage

The contract a host implements so the workbench can open, save and recover files without knowing about browsers, pickers or file systems. No dependencies and no UI.

```sh
dotnet add package ImageSpace.Storage --prerelease
```

**Key types**
- `IEditorStorage`: `OpenAsync`, `SaveAsync` (return `false` on cancel), `ReadRecoveryAsync`, `WriteRecoveryAsync`, `ClearRecoveryAsync`.
- `OpenedFile(Name, Bytes)`: a file chosen by the user.

**Usage**

```csharp
using ImageSpace.Storage;

sealed class FolderStorage(string folder) : IEditorStorage
{
    private string Recovery => Path.Combine(folder, "recovery.imagespace");

    public Task<OpenedFile?> OpenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<OpenedFile?>(null); // show a host file picker here

    public async Task<bool> SaveAsync(string name, byte[] bytes, string contentType, CancellationToken cancellationToken = default)
    {
        await File.WriteAllBytesAsync(Path.Combine(folder, name), bytes, cancellationToken);
        return true; // return false when the user cancels
    }

    public async Task<byte[]?> ReadRecoveryAsync(CancellationToken cancellationToken = default) =>
        File.Exists(Recovery) ? await File.ReadAllBytesAsync(Recovery, cancellationToken) : null;

    public Task WriteRecoveryAsync(byte[] bytes, CancellationToken cancellationToken = default) =>
        File.WriteAllBytesAsync(Recovery, bytes, cancellationToken);

    public Task ClearRecoveryAsync(CancellationToken cancellationToken = default)
    {
        File.Delete(Recovery);
        return Task.CompletedTask;
    }
}
```

### ImageSpace.Imaging

Raster editing primitives: a pressure-aware brush engine (brush, pencil, eraser, clone, dodge, burn, smudge), rectangle/ellipse/polygon/contiguous selections with add/subtract/intersect, O(n) selection morphology, fills and gradients that honor selection coverage, histograms, resize/crop/rotate/flip and the original sample document. Depends on Core; no UI.

```sh
dotnet add package ImageSpace.Imaging --prerelease
```

**Key types**
- `BrushEngine` with `BrushSettings(Size, Hardness, Opacity, Flow, Spacing, Pressure)` and `PaintMode`.
- `Selections`: `Rectangle`, `Polygon`, `Contiguous`, `Combine`, `Invert`, `Bounds`.
- `SelectionMorphology.Apply(...)`: expand, contract, border and smooth; `SelectionContours.Trace` for exact outlines.
- `PixelEdits`: `Fill`, `Gradient`, `Clear`; `MaskOperations` for mask/selection conversion.
- `RasterOperations`: `Resize`, `Crop`, `Rotate90`, `Flip`, `Histogram`, `Sample`.

**Usage**

```csharp
using System.Numerics;
using ImageSpace.Core;
using ImageSpace.Imaging;

var document = new ImageDocument(800, 600);
var layer = Layer.Raster("Paint", 800, 600);
document.Layers.Add(layer);
document.ActiveLayerId = layer.Id;

// Edits respect the document selection's coverage.
var ellipse = Selections.Rectangle(800, 600, new Vector2(100, 100), new Vector2(500, 400), ellipse: true);
document.Selection = SelectionMorphology.Apply(ellipse, SelectionModification.Smooth, radius: 4);
PixelEdits.Gradient(document, layer, new(100, 100), new(500, 400), Rgba32.Parse("#FF8A3D"), Rgba32.Parse("#3D7BFF"));

var brush = new BrushEngine();
brush.Begin(layer.Pixels!, Vector2.Zero);
foreach (var x in new[] { 150f, 250f, 350f })
    brush.Paint(document, layer, new Vector2(x, 250), 0.8f, new BrushSettings(Size: 24), Rgba32.White, PaintMode.Brush);
brush.End();

int[] histogram = RasterOperations.Histogram(layer.Pixels!);
```

### ImageSpace.Filters

Deterministic, allocation-conscious pixel filters on `PixelSurface`: invert, grayscale, sepia, brightness/contrast, saturation, gamma, threshold, posterize, Gaussian blur, sharpen, emboss, edges, pixelate and noise, plus sparse tone-lookup processing for Curves/Levels. Source surfaces are never modified. Depends on Core; no UI.

```sh
dotnet add package ImageSpace.Filters --prerelease
```

**Key types**
- `FilterEngine.Apply(source, kind, amount, secondary, seed)` and `FilterEngine.Blur(source, sigma)`.
- `FilterKind`: the fourteen supported kernels.
- `ToneFilterEngine.Apply(source, RgbLookupTables, cancellationToken)`: cancellable lookup-table processing that preserves alpha.

**Usage**

```csharp
using ImageSpace.Core;
using ImageSpace.Filters;

PixelSurface blurred = FilterEngine.Blur(source, sigma: 3);
PixelSurface sepia = FilterEngine.Apply(source, FilterKind.Sepia);
PixelSurface punchy = FilterEngine.Apply(source, FilterKind.BrightnessContrast, amount: 10, secondary: 20);

var levels = new LevelsAdjustment
{
    Rgb = new LevelsChannel { InputBlack = 16, InputWhite = 240, Gamma = 1.2 },
    Blue = new LevelsChannel { Gamma = 0.95 }
};
PixelSurface toned = ToneFilterEngine.Apply(source, RgbLookupTables.FromLevels(levels), cancellationToken);
```

### ImageSpace.WebGpu

Contracts for optional GPU compute plus the standalone WebGPU JavaScript/WGSL kernels (`WebGpu.js`, shipped as an embedded resource and as `contentFiles/any/any/ImageSpace/WebGpu.js`). Thirteen color/spatial kernels are accelerated: invert, grayscale, sepia, brightness/contrast, saturation, gamma, threshold, posterize, Gaussian blur, sharpen, emboss, edges and pixelate. Seeded Noise and unavailable or over-budget adapters use CPU fallback. Resident sessions retain the original source and intermediate GPU buffers across previews, with explicit execution, final readback and disposal APIs. Depends on Filters; no UI. The host (for example a browser interop layer) implements `IComputeFilterBackend`.

```sh
dotnet add package ImageSpace.WebGpu --prerelease
```

**Key types**
- `IComputeFilterBackend`: `GetCapabilitiesAsync` and `ApplyAsync` (returns `null` for unsupported kernels; never mutates the source).
- `GpuCapabilities(Available, Backend, MaximumBufferSize, Reason)`.
- `GpuKernels.Supports(FilterKind)`: which kernels have a GPU implementation.

**Usage**

```csharp
using ImageSpace.Core;
using ImageSpace.Filters;
using ImageSpace.WebGpu;

static async Task<PixelSurface> ApplyAsync(IComputeFilterBackend gpu, PixelSurface source, FilterKind kind, float amount)
{
    if (GpuKernels.Supports(kind))
    {
        GpuCapabilities caps = await gpu.GetCapabilitiesAsync();
        if (caps.Available && await gpu.ApplyAsync(source, kind, amount, 0) is { } output)
            return output;
    }
    return FilterEngine.Apply(source, kind, amount); // deterministic CPU fallback
}
```

### ImageSpace.Editing

The transactional `EditorSession`: every edit runs against a snapshot, commits as one history entry or rolls back on failure, and undo/redo stays within a memory budget. It also hosts layer, mask, selection, filter and canvas commands. Depends on Imaging and Filters; no UI.

```sh
dotnet add package ImageSpace.Editing --prerelease
```

**Key types**
- `EditorSession`: `Document`, `Execute(name, action)`, `Begin`/`Commit`/`Cancel`, `Undo`/`Redo`, `History`, `HistoryBudget`, `Changed`.
- Layer and canvas commands: `AddLayer`, `DuplicateLayer`, `MoveLayer`, `AddMask`, `ApplyFilter`, `Crop`, `ResizeImage`, `RotateCanvas`.
- Mask commands: `SetMaskProperties`, `SetMaskLinked`, `InvertMask`, `ApplyLayerMask` / `CanApplyLayerMask`.
- `PixelFilterPipeline` and `HistoryEntry`.

**Usage**

```csharp
using ImageSpace.Core;
using ImageSpace.Editing;
using ImageSpace.Filters;

var session = new EditorSession(new ImageDocument(1600, 1000, "Untitled"));
session.Changed += (_, _) => Console.WriteLine($"Revision {session.Revision}");

session.AddLayer("Paint");                             // one history entry
session.FillPixels(new Rgba32(54, 145, 230));
session.ApplyFilter(FilterKind.GaussianBlur, amount: 4);

session.Execute("Add Curves", document =>              // arbitrary edit, one undo step
{
    var adjustment = new Layer
    {
        Name = "Contrast", Kind = LayerKind.Adjustment, Adjustment = AdjustmentKind.Curves,
        Width = document.Width, Height = document.Height,
        Curves = new CurvesAdjustment { Rgb = new ToneCurve { Points = [new(0, 0), new(64, 38), new(192, 218), new(255, 255)] } }
    };
    document.Layers.Add(adjustment);
    document.ActiveLayerId = adjustment.Id;
});
session.Undo();
```

### ImageSpace.Documents

File formats: the versioned native `.imagespace` archive (manifest versions 1–4, minimal version written) for lossless roundtrips, and bounded RGB/8 PSD import/export (raw, PackBits, ZIP and predicted ZIP channels, Unicode names, resolution and user masks). Archive size, dimensions and layer counts are bounded. Depends on Core; no UI.

```sh
dotnet add package ImageSpace.Documents --prerelease
```

**Key types**
- `DocumentArchive.Save(ImageDocument)` / `DocumentArchive.Load(ReadOnlyMemory<byte>)`.
- `PsdCodec.Load(bytes, name)`: returns `PsdCodec.ImportResult(Document, Warnings)`.
- `PsdCodec.Save(document, rasterize, composite, compression)` with `PsdCompression`.

**Usage**

```csharp
using ImageSpace.Core;
using ImageSpace.Documents;
using ImageSpace.Skia;

PsdCodec.ImportResult imported = PsdCodec.Load(File.ReadAllBytes("artwork.psd"), "artwork");
foreach (var warning in imported.Warnings)
    Console.WriteLine(warning);

// Lossless native archive (.imagespace) roundtrip.
byte[] archive = DocumentArchive.Save(imported.Document);
ImageDocument reopened = DocumentArchive.Load(archive);

// PSD export needs rasterized layers and a composite, e.g. from ImageSpace.Skia.
using var renderer = new ImageRenderer();
byte[] psd = PsdCodec.Save(reopened, layer => renderer.RasterizeLayer(reopened, layer),
    renderer.Rasterize(reopened), PsdCompression.Zip);
```

PSD is bounded raster interchange, not lossless Photoshop editing; see [Files and limits](#files-and-limits).

### ImageSpace.Skia

The shared compositor: draws revision-cached tiles, text, shapes, sixteen blend modes, masks and live adjustments (including Curves/Levels lookup filters) into any `SKCanvas`, rasterizes and exports PNG/JPEG/WebP, decodes images and bakes masks. The host picks the Skia backend (GPU or software). Depends on Core and SkiaSharp 3.119; no UI framework.

```sh
dotnet add package ImageSpace.Skia --prerelease
```

**Key types**
- `ImageRenderer`: `Draw`, `DrawLayer`, `Rasterize`, `RasterizePreview`, `RasterizeLayer`, `Export`, `Decode`, `SetTypeface`, `BakeLayerMask`.
- `MaskPreviewRenderer` with `MaskPreviewMode` (composite, grayscale, red overlay).
- `DocumentPreviewCache`: bounded, reusable document previews for thumbnails and histograms.

**Usage**

```csharp
using ImageSpace.Core;
using ImageSpace.Skia;
using SkiaSharp;

using var renderer = new ImageRenderer();

// Composite into any SKCanvas (an Uno SKCanvasElement, an SKSurface, ...).
using var surface = SKSurface.Create(new SKImageInfo(document.Width, document.Height));
renderer.Draw(surface.Canvas, document);

File.WriteAllBytes("poster.webp", renderer.Export(document, SKEncodedImageFormat.Webp, quality: 90));
PixelSurface preview = renderer.RasterizePreview(document, maximumEdge: 256);
PixelSurface decoded = ImageRenderer.Decode(File.ReadAllBytes("photo.jpg"));
```

Use a renderer on its owning thread. To apply a layer mask destructively, pass the renderer to the session: `if (session.CanApplyLayerMask) session.ApplyLayerMask(renderer.BakeLayerMask);`. See the [rendering guide](docs/mask-application-and-rendering.md) for ownership rules.

### ImageSpace.Controls

Self-contained, dark-themed Uno controls used by the workbench and reusable without it: Skia-drawn icons, buttons and menus, numeric fields, an HSV color spectrum, histograms, and the Curves/Levels graph editors with begin/change/complete/cancel semantics. Depends on Core; requires Uno Platform (Skia renderer).

```sh
dotnet add package ImageSpace.Controls --prerelease
```

**Key types**
- `CurveEditor`: `Curve` (`ToneCurve`), `CurveChanged`, `EditCompleted`, `EditCanceled`, `SetHistogram`, `TryBeginEdit`.
- `LevelsControl`: `Value` (`LevelsChannel`), `ValueChanged`, `EditCompleted`, `EditCanceled`, `SetHistogram`.
- `NumericField(label, value, minimum, maximum)`, `ColorSpectrum` (`ColorChanged`), `HistogramView` (`Values`).
- `StudioButton(text, action, icon)`, `IconView(icon)`, `Studio` (theme brushes, `Font`, `Menu`), `AdaptivePanelGrid`.

**Usage**

```csharp
using ImageSpace.Controls;
using ImageSpace.Core;
using Microsoft.UI.Xaml.Controls;

var curve = new CurveEditor { Curve = new ToneCurve { Points = [new(0, 0), new(128, 150), new(255, 255)] } };
curve.CurveChanged += value => ShowPreview(value);     // live, uncommitted
curve.EditCompleted += () => Commit(curve.Curve);
curve.EditCanceled += RevertPreview;

var levels = new LevelsControl { Value = new LevelsChannel { InputBlack = 12, Gamma = 1.1 } };
levels.ValueChanged += channel => ShowPreview(channel);

var opacity = new NumericField("Opacity", 100, 0, 100);
opacity.ValueChanged += value => SetOpacity((float)(value / 100));

window.Content = new StackPanel
{
    Spacing = 8,
    Children = { curve, levels, opacity, new StudioButton("Reset", ResetAll, icon: "undo") }
};
```

### ImageSpace.Editor

`ImageViewport`, the embeddable canvas: Skia compositing with a separate overlay layer, rulers, grid, transform handles, pan/zoom, and pointer gestures for every tool (move, marquee, lasso, wand, crop, brushes, gradient, fill, text, shapes, eyedropper). Each gesture becomes one `EditorSession` transaction. Depends on Editing, Skia and Controls; requires Uno Platform.

```sh
dotnet add package ImageSpace.Editor --prerelease
```

**Key types**
- `ImageViewport(EditorSession)`: `Tool`, `Brush`, `Foreground`, `BackgroundColor`, `Tolerance`, `ShowRulers`, `ShowGrid`, `Renderer`.
- Navigation: `Fit`, `SetZoom`, `Zoom`, `Pan`, `ToDocument`/`ToScreen`.
- Events: `Status`, `ColorPicked`, `TextEditRequested`, `ViewChanged`; `SetMaskPreview(MaskPreviewMode)`.
- `EditorTool`: the 21 tools.

**Usage**

```csharp
using ImageSpace.Core;
using ImageSpace.Editing;
using ImageSpace.Editor;
using ImageSpace.Imaging;

var session = new EditorSession(new ImageDocument(1600, 1000, "Untitled"));
session.AddLayer("Paint");
var viewport = new ImageViewport(session)
{
    Tool = EditorTool.Brush,
    Brush = new BrushSettings(Size: 24, Hardness: 0.9f),
    Foreground = new Rgba32(54, 145, 230)
};
viewport.ColorPicked += color => viewport.Foreground = color;
window.Content = viewport;
```

Dispose the viewport when its window closes.

### ImageSpace.Workbench

The complete ImageSpace workspace as one control: menus, tool bar and options, layers/channels/history/properties panels, the tonal inspector, mask editing, multi-document tabs, filter dialogs and open/save/export commands through `IEditorStorage`. Depends on Editor, Documents and Storage; requires Uno Platform.

```sh
dotnet add package ImageSpace.Workbench --prerelease
```

**Key types**
- `StudioWorkbench(EditorSession, IEditorStorage)`: `Session`, `Surface` (the `ImageViewport`), `Documents`, `AddDocument`, `SelectTool`, `OpenAsync`, `SaveAsync`, `ShowStatus`.
- `StudioWorkbench.GpuFilter`: optional GPU filter delegate (for example an `IComputeFilterBackend.ApplyAsync`); CPU kernels are used otherwise.
- Reusable panels: `LayerPanel`, `PropertyPanel`, `ToneAdjustmentEditor`, `MaskPropertiesEditor`.

**Usage**

```csharp
using ImageSpace.Documents;
using ImageSpace.Editing;
using ImageSpace.Imaging;
using ImageSpace.Storage;
using ImageSpace.Workbench;

IEditorStorage storage = new FolderStorage(dataFolder); // see ImageSpace.Storage
var document = await storage.ReadRecoveryAsync() is { } saved
    ? DocumentArchive.Load(saved)
    : SampleDocument.Create();

var workbench = new StudioWorkbench(new EditorSession(document), storage);
window.Content = workbench;
window.Closed += (_, _) => workbench.Dispose();
```

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
dotnet run --project tests/ImageSpace.FilterStackTests -c Release
dotnet run --project tests/ImageSpace.PreviewTests -c Release
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
