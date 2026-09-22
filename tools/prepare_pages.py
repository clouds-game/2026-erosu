"""为 GitHub Pages 子路径设置 base href，并检查发布目录。"""
import argparse
from pathlib import Path
import re

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("directory", type=Path)
parser.add_argument("--base-path", required=True)
args = parser.parse_args()
if not re.fullmatch(r"/(?:[A-Za-z0-9_.-]+/)*", args.base_path):
  parser.error("base-path must start/end with / and contain only safe path segments")
index = args.directory / "index.html"
html = index.read_text(encoding="utf-8")
if html.count('<base href="/" />') != 1:
  raise ValueError("Expected exactly one base href in the published index.")
index.write_text(html.replace('<base href="/" />', f'<base href="{args.base_path}" />'), encoding="utf-8")
(args.directory / ".nojekyll").touch()
for name in ["interop.js", "game.css", "locales.json", "fonts/ChromaUI-SC.otf", "fonts/ChromaUI-JP.otf", "_framework/blazor.webassembly.js"]:
  if not (args.directory / name).is_file():
    raise FileNotFoundError(name)
print(f"Pages prepared at {args.base_path}")
