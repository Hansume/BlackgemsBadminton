"""Local server for the Unity WebGL build.

Serves Builds/WebGL with the Content-Encoding / Content-Type headers Unity's
compressed builds expect, so the game loads without decompression-fallback warnings.

Usage:  python Tools/serve_webgl.py [--dir Builds/WebGL] [--port 8000]
"""
import argparse
import http.server
import os
import webbrowser

ENCODINGS = {".gz": "gzip", ".br": "br"}
TYPES = {
    ".wasm": "application/wasm",
    ".js": "application/javascript",
    ".data": "application/octet-stream",
    ".symbols.json": "application/octet-stream",
}


class UnityHandler(http.server.SimpleHTTPRequestHandler):
    def guess_type(self, path):
        base, ext = os.path.splitext(path)
        if ext in ENCODINGS:
            for inner, ctype in TYPES.items():
                if base.endswith(inner):
                    return ctype
            return "application/octet-stream"
        return TYPES.get(ext, super().guess_type(path))

    def end_headers(self):
        ext = os.path.splitext(self.path.split("?", 1)[0])[1]
        if ext in ENCODINGS:
            self.send_header("Content-Encoding", ENCODINGS[ext])
        self.send_header("Cache-Control", "no-cache")
        super().end_headers()


def main():
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
    p = argparse.ArgumentParser()
    p.add_argument("--dir", default=os.path.join(root, "Builds", "WebGL"))
    p.add_argument("--port", type=int, default=8000)
    p.add_argument("--no-browser", action="store_true")
    a = p.parse_args()

    directory = os.path.abspath(a.dir)
    if not os.path.isfile(os.path.join(directory, "index.html")):
        raise SystemExit(f"No index.html in {directory} - build the project first (Build > Web > Build WebGL).")

    handler = lambda *args, **kw: UnityHandler(*args, directory=directory, **kw)
    url = f"http://localhost:{a.port}/"
    with http.server.ThreadingHTTPServer(("127.0.0.1", a.port), handler) as httpd:
        print(f"Serving {directory} at {url}  (Ctrl+C to stop)")
        if not a.no_browser:
            webbrowser.open(url)
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
