#!/usr/bin/env python3
"""Fetch the OFL Inter font and its license; no proprietary fonts or artwork."""
from pathlib import Path
from urllib.request import Request, urlopen
root=Path(__file__).resolve().parents[1]
output=root/'src/ImageSpace.App/Assets/Fonts'
output.mkdir(parents=True,exist_ok=True)
base='https://raw.githubusercontent.com/google/fonts/main/ofl/inter/'
for remote,local in [('Inter%5Bopsz,wght%5D.ttf','Inter.ttf'),('OFL.txt','OFL.txt')]:
    path=output/local
    if path.exists(): continue
    with urlopen(Request(base+remote,headers={'User-Agent':'ImageSpace-build'}),timeout=30) as response: data=response.read()
    if not data: raise RuntimeError('Empty font asset response')
    path.write_bytes(data)
    print(f'Fetched {local}: {len(data)} bytes')
