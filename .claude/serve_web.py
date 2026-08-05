"""Static server for the Unity Web builds.

Uses an explicit chdir rather than http.server -d, whose argparse default calls
os.getcwd() before we can chdir - blocked in the sandboxed launch environment.

Serves .br/.gz with the right Content-Encoding, so a build made with
"decompression fallback" OFF (the shipping config) loads here exactly as it
does on itch. Without those headers the loader fails with "Unable to parse".

Usage: serve_web.py [build_dir] [port]   (also reads BUILD_DIR / PORT env vars)
"""
import http.server
import os
import socketserver
import sys

REPO = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
build_dir = sys.argv[1] if len(sys.argv) > 1 else os.environ.get("BUILD_DIR", "Builds/Web-dev")
ROOT = os.path.join(REPO, build_dir)
PORT = int(sys.argv[2]) if len(sys.argv) > 2 else int(os.environ.get("PORT", "8765"))

os.chdir(ROOT)

ENCODINGS = {".br": "br", ".gz": "gzip"}
TYPES = {".wasm": "application/wasm", ".js": "application/javascript",
         ".data": "application/octet-stream", ".symbols.json": "application/octet-stream"}


class Handler(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        # Unity emits e.g. build.wasm.br; the browser needs to be told it is
        # brotli-encoded wasm, not a file of type ".br".
        _, ext = os.path.splitext(self.path.split("?")[0])
        if ext in ENCODINGS:
            self.send_header("Content-Encoding", ENCODINGS[ext])
        super().end_headers()

    def guess_type(self, path):
        base = path[: -len(ext)] if (ext := os.path.splitext(path)[1]) in ENCODINGS else path
        for suffix, mime in TYPES.items():
            if base.endswith(suffix):
                return mime
        return super().guess_type(base)


Handler.extensions_map[".wasm"] = "application/wasm"
Handler.extensions_map[".data"] = "application/octet-stream"

socketserver.TCPServer.allow_reuse_address = True
with socketserver.TCPServer(("127.0.0.1", PORT), Handler) as httpd:
    print(f"serving {os.path.abspath(ROOT)} on http://localhost:{PORT}")
    httpd.serve_forever()
