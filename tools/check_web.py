"""从发布地址检查页面、桥接脚本与真实 WASM 文件；等待 CDN 生效。"""
import argparse
import json
import time
import urllib.request

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("url")
args = parser.parse_args()
base = args.url.rstrip("/") + "/"

def fetch(path, limit=None):
  with urllib.request.urlopen(base + path, timeout=30) as response:
    return response.read() if limit is None else response.read(limit)

for attempt in range(6):
  try:
    assert b"blazor.webassembly.js" in fetch("")
    assert b"KeyChanged" in fetch("interop.js")
    boot = json.loads(fetch("_framework/blazor.boot.json"))
    wasm_files = [name for group in boot["resources"].values() if isinstance(group, dict)
      for name in group if name.endswith(".wasm") and name.startswith("dotnet.native")]
    assert wasm_files, "No WASM runtime in the boot manifest"
    assert fetch("_framework/" + wasm_files[0], 4) == b"\x00asm"
    print(f"Live WASM resources verified: {base}")
    break
  except Exception:
    if attempt == 5:
      raise
    time.sleep(10)
