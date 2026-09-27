#!/usr/bin/env python3
"""Serve a publish tree at the same project prefix used by GitHub Pages."""
import argparse,http.server,mimetypes
from pathlib import Path
parser=argparse.ArgumentParser();parser.add_argument('--port',type=int,default=4173);parser.add_argument('--root',type=Path,default=Path('site'));args=parser.parse_args();root=args.root.resolve()
mimetypes.add_type('application/wasm','.wasm');mimetypes.add_type('application/javascript','.mjs')
class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self,*a,**kw): super().__init__(*a,directory=str(root),**kw)
    def translate_path(self,path):
        if path.startswith('/ImageSpace/'): path=path[len('/ImageSpace'):]
        return super().translate_path(path)
    def end_headers(self):
        self.send_header('Cache-Control','no-store');super().end_headers()
http.server.ThreadingHTTPServer(('127.0.0.1',args.port),Handler).serve_forever()
