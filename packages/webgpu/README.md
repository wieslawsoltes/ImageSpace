# ImageSpace WebGPU

Independent MIT-licensed WebGPU color kernels, extracted from ImageSpace. No Uno, .NET, Adobe SDK, UI framework or proprietary shader compiler is required by this JavaScript package.

Build a local tarball from the repository with `npm run pack:webgpu`. Registry publication is separate.

```js
import { initialize, applyFilter, capabilities } from '@wieslawsoltes/imagespace-webgpu';

await initialize();
const output = await applyFilter(rgbaBytes, width, height, 'Invert', 0, 0);
if (output === null) {
  // A compatible adapter/kernel is unavailable; use your application's CPU path.
}
console.log(capabilities());
```

Inputs are straight-alpha RGBA8 `Uint8Array` values. Supported kernels: Invert, Grayscale, Sepia, BrightnessContrast, Saturation, Gamma, Threshold and Posterize. Amount/secondary parameters follow the ImageSpace CPU kernel conventions. Unsupported adapters/kernels return `null`; malformed input or a runtime failure throws.

The implementation validates dimensions and buffer limits, uses 16 × 16 compute workgroups, handles device loss, and destroys per-operation buffers after readback. It is a transfer/readback backend, not a resident-texture editor or a guarantee of faster execution for every image size. HTTPS or localhost is required. Physical GPU/driver testing remains the responsibility of the consuming application.
