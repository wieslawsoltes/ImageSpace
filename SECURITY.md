# Security and data handling

ImageSpace edits local files. The application has no document-upload service, account system, embedded analytics or Adobe cloud integration. The browser downloads runtime/assets from its hosting origin. A browser-origin recovery record is not encrypted and is not a cloud backup.

Treat image/document parsers as untrusted-input boundaries. Current limits include 128 MiB input/native archives, 384 MiB expanded native archive/layer data, 16 megapixels per surface, 8192 pixels per dimension, 128 layers, bounded JSON depth/manifest size, unique archive entries and validated PSD section lengths. Native ZIP files are read in memory by exact entry name, not extracted into arbitrary filesystem paths.

The limits reduce risk but do not guarantee that every document fits every browser's memory budget. Native codecs and GPU drivers remain part of the attack surface. Keep runtime packages updated in ABI-compatible sets. Do not use this alpha release as the only copy of important work.

The `?test=1` diagnostics surface is read-only and opt-in. It reports document metadata and control geometry but does not execute arbitrary commands. CI Pages deployment checks artifact origin, source SHA and WebAssembly payload. Production registry credentials are only used by tag-release workflows.

Report suspected vulnerabilities privately through GitHub security advisories for this repository rather than posting a weaponized sample in a public issue. Include the affected revision, host, input dimensions/format and the smallest non-sensitive reproducer.
