"""从发布地址检查页面、桥接脚本与真实 WASM 文件；等待 CDN 生效。"""
import argparse
import json
import time
import urllib.request


def fetch(base, path, limit=None):
  with urllib.request.urlopen(base.rstrip("/") + "/" + path, timeout=30) as response:
    return response.read() if limit is None else response.read(limit)


def verify_web(base):
  assert b"blazor.webassembly.js" in fetch(base, "")
  assert b"KeyChanged" in fetch(base, "interop.js")
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
