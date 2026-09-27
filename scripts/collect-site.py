#!/usr/bin/env python3
"""Collect the real Uno publish tree, retaining runtime assets and adding host chrome styles."""
import argparse
import json
import os
import shutil
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
args.output.mkdir(parents=True, exist_ok=True)
shutil.copytree(source, args.output, dirs_exist_ok=True)

# Uno single-project does not automatically include an arbitrary WasmCSS folder.
# Preserve its generated bootstrap/index and append the app's own stylesheet.
stylesheet = Path(__file__).resolve().parents[1] / 'src/ImageSpace.App/Platforms/WebAssembly/WasmCSS/Style.css'
shutil.copyfile(stylesheet, args.output / 'imagespace.css')
index = args.output / 'index.html'
html = index.read_text(encoding='utf-8')
if 'href="./imagespace.css"' not in html:
    if '</head>' not in html:
        raise SystemExit('Uno index has no closing head element')
    html = html.replace('</head>', '<link rel="stylesheet" href="./imagespace.css" />\n</head>', 1)
index.write_text(html, encoding='utf-8')
(args.output / '.nojekyll').touch()
(args.output / 'build-info.json').write_text(json.dumps({
    'application': 'ImageSpace',
    'host': 'Uno WebAssembly',
    'version': os.environ.get('VERSION', '0.1.0-alpha.1'),
    'commit': os.environ.get('GITHUB_SHA', 'local')
}))
print(f'Collected real Uno application from {source}')
