#!/usr/bin/env python3
"""Find and validate the actual Uno published root; never publish a placeholder."""
import argparse,json,os,shutil
from pathlib import Path
parser=argparse.ArgumentParser();parser.add_argument('publish',type=Path);parser.add_argument('output',type=Path);args=parser.parse_args()
candidates=sorted(args.publish.rglob('index.html'),key=lambda p:len(p.parts))
if not candidates: raise SystemExit('Uno publish produced no index.html')
source=candidates[0].parent
if not list(source.rglob('*.wasm')): raise SystemExit('The publish output has no WebAssembly runtime or assemblies')
args.output.mkdir(parents=True,exist_ok=True);shutil.copytree(source,args.output,dirs_exist_ok=True)
(args.output/'.nojekyll').touch()
(args.output/'build-info.json').write_text(json.dumps({'application':'ImageSpace','host':'Uno WebAssembly','version':os.environ.get('VERSION','0.1.0-alpha.1'),'commit':os.environ.get('GITHUB_SHA','local')}))
print(f'Collected real Uno application from {source}')
