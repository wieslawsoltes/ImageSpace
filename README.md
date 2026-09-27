# ImageSpace

**A local-first, layered image editor for desktop and browser.**

ImageSpace is an independent C# editor built on Uno Platform 6.7 and a GPU-capable Skia compositor. The workspace follows familiar professional photo-editing conventions: compact menus and tool options, a vertical toolbox, document tabs, rulers, color controls, properties, layers and history.

> Initial development release. This is not Adobe Photoshop, not affiliated with Adobe, and not a claim of complete Photoshop compatibility. See the feature matrix and architecture documentation for implemented behavior and explicit limits.

## Reusable libraries

The document model, copy-on-write tile store, brush/selection engine, filter kernels, transactional editing, file formats, renderer, custom controls and viewport are separate packable libraries. No Adobe code, icons, fonts, stock assets or cloud services are used.

## License

ImageSpace source is MIT licensed. Uno Platform is Apache-2.0; SkiaSharp is MIT; Skia is BSD-3-Clause. Third-party notices are retained separately.
