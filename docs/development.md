# Development, validation and releases

## Toolchain

Use the pinned `global.json`: .NET SDK 10.0.401 and Uno SDK 6.7.30. Install `wasm-tools` for browser builds. Python 3 is used for deterministic asset acquisition and static-site collection. Node 22 and the locked Playwright dependency are used only for browser acceptance and the standalone WebGPU package.

`python3 scripts/fetch-assets.py` downloads Inter and its OFL notice from Google Fonts. The source archive deliberately does not vendor a proprietary/system font. Network access is required for package restore and this initial font download. The running editor does not upload image documents.

Linux desktop requires a supported display server and the native libraries expected by Uno's X11/Skia host. Headless engine tests use the matching `SkiaSharp.NativeAssets.Linux.NoDependencies` package; a real desktop run still needs its display/input integration. macOS and Windows developer builds are not signed or notarized by the default workflow.

## Local commands

```sh
python3 scripts/fetch-assets.py
dotnet workload install wasm-tools --skip-manifest-update
dotnet build ImageSpace.slnx -c Release

dotnet run --project tests/ImageSpace.Tests -c Release
dotnet run --project tests/ImageSpace.RegressionTests -c Release

dotnet publish src/ImageSpace.App -c Release -f net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/ImageSpace/ -o artifacts/publish
python3 scripts/collect-site.py artifacts/publish site
npm ci
npx playwright install chromium
npm run test:browser
```

For a desktop-only build, pass `-p:ImageSpaceDesktopOnly=true -f net10.0-desktop` to the app project. To self-contain a host, also provide `-r win-x64`, `-r linux-x64` or `-r osx-arm64` and `--self-contained true`.

## Browser diagnostics

Appending `?test=1` opts into read-only `imageSpaceDiagnostics` and `imageSpaceControls` JavaScript objects and disables recovery restoration for reproducible tests. Controls are discovered from the real Uno visual tree, including open popups. The harness then uses real browser pointer and keyboard input; no JavaScript command endpoint mutates the C# model.

Screenshots are decoded losslessly in the test harness. Acceptance checks require nonempty artwork and test that brush pixels change and return after undo. Native archive saving is exercised through an actual browser download and reopened through the file chooser. GPU results report adapter availability separately from UI/correctness success.

## GitHub workflows

- **Build:** engine and raster regressions, browser publish, real-browser acceptance, dual-target library builds, NuGet/WebGPU packages, source and validation artifacts.
- **Desktop:** independent Windows, Linux and macOS self-contained host builds.
- **GitHub Pages:** consumes only a successful non-PR main Build artifact, verifies its commit and WebAssembly content, deploys it, verifies the public `build-info.json` SHA and repeats browser acceptance on the deployed URL.
- **Release:** tags `v*` build and attach source, packages, browser and native artifacts; registry publication is conditional on explicit credentials.
- **Format source:** explicit source-formatting maintenance; uses the repository's EditorConfig.

The Pages workflow never substitutes a hand-written HTML landing page for a failed Uno application. Deployment identity is recorded in `build-info.json`. A successful compile is not treated as proof that the browser paints pixels or that the public site serves the intended commit.

## Package distribution

All eleven library projects are packable. Uno libraries target both `net10.0-browserwasm` and `net10.0-desktop`; engine libraries target `net10.0`. Use CI-produced `.nupkg` files from a local NuGet feed, or reference projects directly. Keep the managed/native Skia ABI matched in consuming hosts.

The standalone browser kernel package can be built with:

```sh
npm run pack:webgpu
npm install ./artifacts/packages/wieslawsoltes-imagespace-webgpu-0.1.0-alpha.1.tgz
```

For public registry releases, configure `NUGET_API_KEY` and/or `NPM_TOKEN` in repository secrets. The default workflow does not fabricate credentials or report skipped registry publication as a release success. Signing/notarization is separate and must be configured for production native distribution.

## Change policy

Add a deterministic regression for fixed model/rendering defects. Keep document invariants in Core and editing commands transactional. Do not put browser APIs in the model or introduce a second source of truth for GPU pixels. Document unsupported Photoshop semantics, format loss and backend fallback explicitly. Never include Adobe assets or claim full parity based on a screenshot alone.
