"""检查 Pages 入口、实际发布提交和各分支的 WASM 资源。"""
import argparse
import json
from pathlib import Path
import time

from check_web import fetch, retry, verify_web
from pages_site import validate_snapshot


def verify_pages(base, expected):
  manifest = validate_snapshot(json.loads(fetch(base, f"branches.json?check={time.time_ns()}")))
  if manifest != expected:
    raise ValueError("The live branch commits do not match this build's snapshot")
  html = fetch(base, "")
  assert b"selector.js" in html, "Missing branch selector"
  assert b"selector.css" in html, "Missing selector styles"
  assert fetch(base, "selector.js"), "Empty selector script"
  assert fetch(base, "selector.css"), "Empty selector styles"
  for branch in manifest["branches"]:
    url = base.rstrip("/") + "/" + branch["path"]
    metadata = json.loads(fetch(url, f"build-info.json?check={time.time_ns()}"))
    if metadata["commit"] != branch["commit"] or metadata["name"] != branch["name"]:
      raise ValueError(f"Unexpected build at {url}")
    verify_web(url)
  print(f"Live branch selector and commits verified: {base}")


def main():
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument("url")
  parser.add_argument("--snapshot", type=Path, required=True)
  args = parser.parse_args()
  expected = validate_snapshot(json.loads(args.snapshot.read_text(encoding="utf-8")))
  retry(lambda: verify_pages(args.url, expected))


if __name__ == "__main__":
  main()
