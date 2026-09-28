#!/usr/bin/env python3
"""Collect a real Uno publish tree and reject malformed generated JavaScript before deployment."""
import argparse
import json
import os
import shutil
import subprocess
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('publish', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
candidates = sorted(args.publish.rglob('index.html'), key=lambda path: len(path.parts))
if not candidates:
    raise SystemExit('Uno publish produced no index.html')
source = candidates[0].parent
if not list(source.rglob('*.wasm')):
    raise SystemExit('The publish output has no WebAssembly runtime or assemblies')
manifests = list(source.rglob('AppManifest.js'))
if len(manifests) != 1:
    raise SystemExit('Expected exactly one generated Uno application manifest')
# Validate generated files, not only source: Uno transforms manifest properties.
for pattern in ('AppManifest.js', 'Host.js', 'WebGpu.js'):
    files = list(source.rglob(pattern))
    if len(files) != 1:
        raise SystemExit(f'Expected one shipped {pattern}')
    subprocess.run(['node', '--check', str(files[0])], check=True)

args.output.mkdir(parents=True, exist_ok=True)
shutil.copytree(source, args.output, dirs_exist_ok=True)
stylesheet = Path(__file__).resolve().parents[1] / 'src/ImageSpace.App/Platforms/WebAssembly/WasmCSS/Style.css'
shutil.copyfile(stylesheet, args.output / 'imagespace.css')
index = args.output / 'index.html'
html = index.read_text(encoding='utf-8')
if 'href="./imagespace.css"' not in html:
    if '</head>' not in html:
        raise SystemExit('Uno index has no closing head element')
    html = html.replace('</head>', '<link rel="stylesheet" href="./imagespace.css" />\n</head>', 1)
index.write_text(html, encoding='utf-8')
# Do not leave precompressed variants containing an older copy of an edited file.
for suffix in ('.br', '.gz'):
    index.with_name(index.name + suffix).unlink(missing_ok=True)
(args.output / '.nojekyll').touch()
(args.output / 'build-info.json').write_text(json.dumps({
    'application': 'ImageSpace',
    'host': 'Uno WebAssembly',
    'version': os.environ.get('VERSION', '0.3.0-alpha.1'),
    'commit': os.environ.get('GITHUB_SHA', 'local')
}))
print(f'Collected and syntax-checked real Uno application from {source}')
