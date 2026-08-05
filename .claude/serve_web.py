"""Static server for the WebGL dev build (avoids http.server -d, whose
argparse default calls os.getcwd() before we can chdir - blocked in the
sandboxed launch environment)."""
import http.server
import os
import socketserver

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Builds", "Web-dev")
os.chdir(ROOT)

Handler = http.server.SimpleHTTPRequestHandler
Handler.extensions_map[".wasm"] = "application/wasm"
Handler.extensions_map[".data"] = "application/octet-stream"

socketserver.TCPServer.allow_reuse_address = True
with socketserver.TCPServer(("127.0.0.1", 8765), Handler) as httpd:
    print(f"serving {os.path.abspath(ROOT)} on http://localhost:8765")
    httpd.serve_forever()
