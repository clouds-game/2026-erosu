"""构建独立桌面包；无需玩家安装 Godot 或 .NET。"""

import argparse
import hashlib
import os
from pathlib import Path
import platform
import plistlib
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
    with (output / "Contents/Info.plist").open("rb") as source:
      executable = output / "Contents/MacOS" / plistlib.load(source)["CFBundleExecutable"]
  else:
    executable = output
  smoke = subprocess.run([str(executable), "--headless", "--quit-after", "120"], cwd=ROOT,
    check=True, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=90)
  log = smoke.stdout + smoke.stderr
  print(log)
  if "ERROR:" in log or "SCRIPT ERROR:" in log:
    raise RuntimeError("The exported game reported runtime errors during its startup check.")

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
  with zipfile.ZipFile(archive, "a", compression=zipfile.ZIP_DEFLATED) as package:
    package.write(ROOT / "Assets/Fonts/OFL.txt", "FONT-LICENSE.txt")
    package.write(ROOT / "Assets/Fonts/README.md", "FONT-SOURCES.md")
  digest = hashlib.sha256(archive.read_bytes()).hexdigest()
  archive.with_suffix(".zip.sha256").write_text(f"{digest}  {archive.name}\n", encoding="utf-8")
  print(f"Package: {archive}")


if __name__ == "__main__":
  main()
