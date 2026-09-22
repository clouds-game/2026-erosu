"""构建独立桌面包；无需玩家安装 Godot 或 .NET。"""

import argparse
import hashlib
import os
from pathlib import Path
import platform
import shutil
import subprocess
import zipfile


ROOT = Path(__file__).resolve().parents[1]


def run(*command):
  subprocess.run(command, cwd=ROOT, check=True)


def main():
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument("target", choices=["windows", "macos"])
  parser.add_argument("--godot", default=os.environ.get("GODOT_BIN", "godot"))
  args = parser.parse_args()
  if args.target == "macos" and platform.system() != "Darwin":
    parser.error("The macOS package uses Apple's codesign and must be built on macOS.")
  build = ROOT / "build" / args.target
  if build.exists():
    shutil.rmtree(build)
  build.mkdir(parents=True)
  run("dotnet", "build", "ChromaDrop.csproj", "--configuration", "ExportRelease", "--nologo")
  run(args.godot, "--headless", "--path", str(ROOT), "--editor", "--import", "--quit")
  output = build / ("ChromaDrop.exe" if args.target == "windows" else "ChromaDrop.app")
  preset = "Windows" if args.target == "windows" else "macOS"
  run(args.godot, "--headless", "--path", str(ROOT), "--export-release", preset, str(output))

  if not output.exists() or not list(build.rglob("ChromaDrop.dll")):
    raise RuntimeError("The export is missing its executable or C# assembly.")
  if args.target == "macos":
    run("codesign", "--verify", "--deep", "--strict", str(output))
    run(str(output / "Contents/MacOS/ChromaDrop"), "--headless", "--quit-after", "120")
  else:
    run(str(output), "--headless", "--quit-after", "120")

  packages = ROOT / "build" / "packages"
  packages.mkdir(parents=True, exist_ok=True)
  archive = packages / f"chroma-drop-{args.target}-{'x64' if args.target == 'windows' else 'universal'}.zip"
  if args.target == "macos":
    run("ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", str(output), str(archive))
  else:
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as package:
      for path in sorted(build.rglob("*")):
        if path.is_file():
          package.write(path, path.relative_to(build))
  digest = hashlib.sha256(archive.read_bytes()).hexdigest()
  archive.with_suffix(".zip.sha256").write_text(f"{digest}  {archive.name}\n", encoding="utf-8")
  print(f"Package: {archive}")


if __name__ == "__main__":
  main()
