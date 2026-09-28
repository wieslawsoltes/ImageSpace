# Compatibility and raster performance — 0.2 alpha

## PSD contract

The importer accepts PSD version 1, eight-bit RGB. Every nested resource, layer, mask, additional-information block and channel is read through a section-local bounded reader. A channel cannot read into its neighbor even if its declared compression or dimensions are malicious. Input is limited to 128 MiB, dimensions to the existing 8192-per-side/16-megapixel limits, layers to 128, channels to 16, and cumulative decoded processing data to 384 MiB.

Raw, PackBits, ZIP and ZIP prediction are accepted for pixel and simple user-mask channels. Eight-bit ZIP prediction is reversed separately for each row using modulo-256 prefix sums. Decompression must produce exactly the expected number of bytes; expansion past dimensions is rejected. The merged-image decoder recognizes the negative layer-count transparency marker, rather than treating every fourth channel as transparency.

Unicode `luni` names use strict big-endian UTF-16, bounded to 1024 code units. The exporter writes both a Pascal compatibility name and a Unicode name. ResolutionInfo is retained as the document's DPI; unequal horizontal/vertical resolution emits a warning because the application has one DPI value.

Simple `-2` user-mask channels are placed into layer-local coverage. Relative/absolute positioning, default outside coverage, disabled/inverted flags and supported density/feather metadata are handled. The source mask rectangle is not retained as an independently movable mask: it is rebased into the raster layer's extent. The application uses a Gaussian interpretation for feather, reports that boundary, and rejects values outside 0–32 rather than silently truncating them. Combined vector/real-user mask semantics, groups, clipping chains, editable Photoshop text/adjustments, smart objects, layer effects, high-bit modes, PSB and ICC profile application remain unsupported and are reported.

Export rasterizes application-specific type, shape, transform and mask content through the existing callback and emits standard RGB/8 pixel layers. ZIP is the default; raw, PackBits and ZIP prediction are selectable through `PsdCompression`. Only one callback surface is retained at a time. Encoded-channel accumulation and output writes are bounded. The merged composite is stored as one planar zlib stream, not a concatenation of independently headed channel streams. Visible adjustment documents still use the application's flattened compatibility export.

Reference: Adobe, *Photoshop File Formats Specification*, sections “Image Resources,” “Layer and Mask Information,” “Additional Layer Information,” and “Image Data”: https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/

## Tile and row APIs

`PixelSurface.CopyRowTo` and `WriteRow` operate on contiguous caller-owned RGBA spans and validate the complete run before mutation. Writes clone only affected shared tiles. Unchanged writes retain tile identity and revision, preserving GPU-image cache hits. An aliased source that overlaps a later segment is snapshotted before mutation; ordinary row transfers allocate no temporary run.

`Fill` uses packed, vectorizable tile fills with zero edge padding. `FromRgba` imports directly by tile and supports padded row strides. `CopyToRgba` preserves caller padding. Alpha-only transfer methods support compact coverage processing. Revisions are monotonic content stamps, not literal counts of pixel operations.

Histograms visit allocated tiles without per-pixel dictionary lookup. Crop and flip transfer rows; bilinear resize caches neighboring rows in pooled buffers and uses premultiplied interpolation to avoid dark transparent fringes. Sampling no longer requires per-call temporary arrays. Skia tile upload and decoded/readback pixels use bounded native spans, eliminating the previous tile-sized and full-image managed transfer arrays.

Native archives stream row data into and out of ZIP entries rather than staging a second full RGBA copy for every layer. Exact entry lengths, metadata limits, duplicate entries and existing version-1/2/3 semantics remain validated. History and transient selections are not silently added to the archive format.

## Selection morphology

Expand and Contract compute grayscale maximum/minimum using separable monotonic deques. Border is maximum minus minimum. Smooth is opening followed by closing. Soft coverage is preserved. The square neighborhood has radius 0–256, and out-of-canvas coverage is zero. Runtime is linear in pixel count rather than proportional to radius squared. Cancellation is checked between scan lines; the source selection remains unchanged until the transactional editor command commits.

The actual Uno Select menu exposes all four operations with numeric radius input. Each successful operation makes exactly one undo entry. Invalid parameters, cancellation and stale dialog state do not create a partial edit.

## Verification and performance interpretation

The compatibility suite includes independent PSD fixtures, compression and Unicode roundtrips, user-mask positioning/flags, decompression overruns, truncated sections, duplicate channels, invalid text, padded/aliased raster transfers, snapshot isolation, scalar differential resampling/morphology tests and selection history checks. Browser tests use real file pickers and custom Uno menu controls, not mutation hooks.

`tests/ImageSpace.Benchmarks` includes the frozen pre-optimization tile and raster implementations from `494efd1ad31ce7323fde94839d385b24a82e1aef`. Both implementations run in the same Release process with deterministic 1024×1024 input, two warmups and seven alternating measurements. The report contains medians and thread allocations. This controls for many environment differences but is not a universal latency guarantee. Modern .NET can eliminate some temporary allocations in the baseline; reported allocations, not source-code array counts, are the evidence.

Run `dotnet run --project tests/ImageSpace.Benchmarks -c Release`. CI retains `artifacts/performance-results.json`. These measurements concern CPU raster operations only. Neither a software WebGPU adapter nor CPU submission counters establish physical GPU speed, presentation latency or Photoshop-wide performance parity.
