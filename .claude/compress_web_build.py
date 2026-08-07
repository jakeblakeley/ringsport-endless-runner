"""Brotli-compress a Unity Web build in place.

Unity skips compression entirely for Development builds, so a debug build ships
a ~112 MB wasm and ~49 MB data file. That is fine locally and painful over the
wire, especially on a phone.

This compresses those files, renames them with a .br suffix, and rewrites the
URLs in index.html to match. The browser decompresses transparently via the
Content-Encoding header, so Unity's loader never knows the difference - it asks
for the .br URL and receives ordinary wasm. The header comes from the _headers
file the WebGL template already ships, which Netlify honours.

Only touches files that are not already compressed, so re-running is harmless.

  python3 .claude/compress_web_build.py Builds/Web-webgpu
"""
import os
import re
import subprocess
import sys

# The loader fetches these by URL from index.html; TemplateData is small enough
# that Netlify's own on-the-fly compression covers it.
TARGET_EXT = (".wasm", ".data", ".js", ".symbols.json")


def main(build_root):
    build_dir = os.path.join(build_root, "Build")
    index = os.path.join(build_root, "index.html")
    if not os.path.isdir(build_dir):
        sys.exit(f"no Build/ directory under {build_root}")
    if not os.path.isfile(index):
        sys.exit(f"no index.html under {build_root}")

    html = open(index, encoding="utf-8").read()
    renames, before, after = [], 0, 0

    for name in sorted(os.listdir(build_dir)):
        path = os.path.join(build_dir, name)
        if name.endswith(".br") or name.endswith(".gz") or not os.path.isfile(path):
            continue
        if not name.endswith(TARGET_EXT):
            continue

        src_size = os.path.getsize(path)
        out = path + ".br"
        # -q 5 keeps this to seconds rather than minutes; the marginal ratio
        # above that is small and this is a throwaway debug artifact.
        subprocess.run(["brotli", "-f", "-q", "5", "-o", out, path], check=True)
        os.remove(path)

        before += src_size
        after += os.path.getsize(out)
        renames.append((name, name + ".br"))

    if not renames:
        print("nothing to compress (already done?)")
        return

    for old, new in renames:
        if old not in html:
            print(f"  WARNING: {old} not referenced in index.html")
        html = html.replace(old, new)
    open(index, "w", encoding="utf-8").write(html)

    print(f"compressed {len(renames)} file(s): {before/1e6:.1f} MB -> {after/1e6:.1f} MB "
          f"({100 * after / before:.0f}% of original)")
    for old, new in renames:
        print(f"  {old} -> {new}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "Builds/Web-webgpu")
