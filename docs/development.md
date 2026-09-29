# Development, validation and releases

## Toolchain

Use the pinned `global.json`: .NET SDK 10.0.401 and Uno SDK 6.7.30. Install `wasm-tools` for browser builds. Python 3 is used for deterministic asset acquisition and static-site collection. Node 22 and the locked Playwright dependency are used for browser acceptance and the standalone WebGPU package.

`python3 scripts/fetch-assets.py` downloads Inter and its OFL notice from Google Fonts. The source archive does not vendor a proprietary/system font. Network access is required for restore and this initial font download. The running editor does not upload image documents.

Linux desktop requires a supported display server and the native libraries expected by Uno's X11/Skia host. Headless tests use the matching `SkiaSharp.NativeAssets.Linux.NoDependencies` package; a desktop run still needs its display/input integration. macOS and Windows developer builds are not signed or notarized by the default workflow.

## Local commands

```sh
python3 scripts/fetch-assets.py
dotnet workload install wasm-tools --skip-manifest-update
dotnet build ImageSpace.slnx -c Release

dotnet run --project tests/ImageSpace.Tests -c Release
dotnet run --project tests/ImageSpace.RegressionTests -c Release
dotnet run --project tests/ImageSpace.MaskTests -c Release
dotnet run --project tests/ImageSpace.MaskEditingTests -c Release
dotnet run --project tests/ImageSpace.CompatibilityTests -c Release
dotnet run --project tests/ImageSpace.LayerTests -c Release
dotnet run --project tests/ImageSpace.Benchmarks -c Release

dotnet publish src/ImageSpace.App -c Release -f net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/ImageSpace/ -o artifacts/publish
python3 scripts/collect-site.py artifacts/publish site
npm ci
npx playwright install chromium
npm run test:browser
```

For a desktop-only build, pass `-p:ImageSpaceDesktopOnly=true -f net10.0-desktop` to the app project. For a self-contained host also provide `-r win-x64`, `-r linux-x64` or `-r osx-arm64` and `--self-contained true`.

## Browser diagnostics

Appending `?test=1` opts into read-only `imageSpaceDiagnostics` and `imageSpaceControls` JavaScript objects and disables recovery restoration for reproducible tests. Controls are discovered from the real Uno visual tree, including open popups. The harness uses real pointer and keyboard input; no JavaScript command endpoint mutates the C# model.

Screenshots are decoded losslessly in the test harness. Acceptance requires nonempty artwork and verifies painting, undo, tonal/mask edits, mask application, and native save/reopen through real downloads and file choosers. GPU results report adapter availability separately from UI/correctness success. Browser application metadata must match `build-info.json`.

## Rendering validation

`ImageSpace.LayerTests` exercises direct drawing against `EnableDirectLayerDrawing = false`, the always-isolated path. It checks every existing blend mode on raster, text and shape content, transforms/reflections, masks, fractional group opacity, warm tile reuse and canvas save balance. Mask-application cases test authored dimensions/RGB, off-canvas source pixels, affine coverage, selection independence, history, callback failures/staleness, result ownership and native persistence.

It writes `artifacts/layer-tests.json` and `artifacts/layer-performance-results.json`. The benchmark is a warmed same-process raster CPU comparison with alternating samples. It does not establish GPU performance or application frame rate; no machine-dependent performance threshold is used as a correctness assertion. See [mask application and rendering](mask-application-and-rendering.md).

## GitHub workflows

- **Build:** all engine/raster/layer regressions and benchmarks, browser publish, real-browser acceptance, dual-target library builds, NuGet/WebGPU packages, source and validation artifacts.
- **Desktop:** independent Windows, Linux and macOS self-contained host builds.
- **GitHub Pages:** consumes only a successful non-PR main Build artifact, verifies its commit and WebAssembly content, deploys it, verifies public `build-info.json`, and repeats browser acceptance on the deployed URL.
- **Release:** `v*` tags (or manual dry runs with a version input) validate and build source, packages, browser and self-contained single-file desktop executables for win/linux/osx x64 and arm64 with `SHA256SUMS`. Tags attach them to a GitHub Release and publish the NuGet packages via NuGet.org Trusted Publishing from the `nuget` environment; manual runs publish nothing.
- **Format source:** explicit maintenance using the repository's EditorConfig.

The Pages workflow never substitutes a hand-written HTML landing page for a failed Uno application. Deployment identity is recorded in `build-info.json`. Compilation is not treated as proof that the browser paints pixels or that the public site serves the intended commit.

## Package distribution

All eleven library projects are packable. Uno libraries target both `net10.0-browserwasm` and `net10.0-desktop`; engine libraries target `net10.0`. Tagged releases publish them to NuGet.org; alternatively use CI-produced `.nupkg` files from a local NuGet feed or reference projects directly. Keep the managed/native Skia ABI matched in consuming hosts.

Build the standalone browser package with:

```sh
npm run pack:webgpu
npm install ./artifacts/packages/wieslawsoltes-imagespace-webgpu-0.3.2-alpha.1.tgz
```

NuGet.org publication uses [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): the tag-only `nuget` job in `release.yml` exchanges a GitHub OIDC token for a short-lived key in the protected `nuget` environment, so no NuGet API key is stored. npm publication of the WebGPU tarball still requires an `NPM_TOKEN` secret and runs only for tags. Signing/notarization is separate and must be configured for production native distribution.

## Change policy

Add deterministic regressions for fixed model/rendering defects. Keep document invariants in Core and editing commands transactional. Do not put browser APIs in the model or introduce a second source of truth for GPU pixels. Document unsupported Photoshop semantics, format loss, sampling/rounding boundaries and backend fallback. Never include Adobe assets or claim full parity based on a screenshot.

## UI responsiveness regressions

See [UI responsiveness](ui-responsiveness.md) for retained inspector/row ownership, deferred preview caches, invalidation categories, and read-only performance counters. Browser validation includes warmed real-pointer selection, canvas no-op clicks, cursor/ants isolation, numeric edits after undo and tone/channel preview reuse.
