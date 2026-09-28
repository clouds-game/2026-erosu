"""从发布地址检查页面、桥接脚本与真实 WASM 文件；等待 CDN 生效。"""
import argparse
import json
import time
import urllib.request

from pages_site import BaseParser, GAME0_ASSET_FILES


def fetch(base, path, limit=None):
  with urllib.request.urlopen(base.rstrip("/") + "/" + path, timeout=30) as response:
    return response.read() if limit is None else response.read(limit)


def verify_web(base):
  html = fetch(base, "")
  assert b"blazor.webassembly.js" in html
  interop = fetch(base, "interop.js")
  assert b"KeyChanged" in interop
  parser = BaseParser()
  parser.feed(html.decode("utf-8"))
  if parser.game == "game0":
    assert b"connectGame0" in interop, "Missing Game0 pointer bridge"
    for name in GAME0_ASSET_FILES:
      if name.endswith(".png"):
        assert fetch(base, name, 8) == b"\x89PNG\r\n\x1a\n", f"Invalid Game0 image: {name}"
      else:
        assert fetch(base, name), f"Missing Game0 attribution: {name}"
  catalog = json.loads(fetch(base, "locales.json"))
  assert set(catalog) == {"en", "zh-CN", "ja"}
  for language in ("SC", "JP"):
    assert fetch(base, f"fonts/ChromaUI-{language}.otf", 4) == b"OTTO"
  boot = json.loads(fetch(base, "_framework/blazor.boot.json"))
  wasm_files = [name for group in boot["resources"].values() if isinstance(group, dict)
    for name in group if name.endswith(".wasm") and name.startswith("dotnet.native")]
  assert wasm_files, "No WASM runtime in the boot manifest"
  assert fetch(base, "_framework/" + wasm_files[0], 4) == b"\x00asm"
  print(f"Live WASM resources verified: {base}")


def retry(verify):
  for attempt in range(6):
    try:
      verify()
      return
    except Exception:
      if attempt == 5:
        raise
      time.sleep(10)


def main():
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument("url")
  args = parser.parse_args()
  retry(lambda: verify_web(args.url))


if __name__ == "__main__":
  main()
