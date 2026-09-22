"""安装与 C# SDK 同版本的官方编辑器、导出模板，并验证 SHA-512。"""

import argparse
import hashlib
import os
from pathlib import Path
import platform
import shutil
import stat
import urllib.request
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parents[1]


def extract(archive, destination):
  with zipfile.ZipFile(archive) as package:
    for info in package.infolist():
      target = (destination / info.filename).resolve()
      if not target.is_relative_to(destination.resolve()):
        raise ValueError(f"Unsafe archive member: {info.filename}")
      package.extract(info, destination)
      mode = info.external_attr >> 16
      if mode and not info.is_dir():
        target.chmod(stat.S_IMODE(mode))


def main():
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument("--templates-only", action="store_true")
  parser.add_argument("--install-dir", type=Path, default=ROOT / ".local" / "godot")
  args = parser.parse_args()
  version = ET.parse(ROOT / "ChromaDrop.csproj").getroot().attrib["Sdk"].split("/")[1]
  release = f"{version}-stable"
  base = f"https://github.com/godotengine/godot-builds/releases/download/{release}"
  install = args.install_dir.resolve()
  install.mkdir(parents=True, exist_ok=True)
  with urllib.request.urlopen(f"{base}/SHA512-SUMS.txt", timeout=60) as response:
    checksums = {line.split()[1].lstrip("*"): line.split()[0] for line in response.read().decode().splitlines() if line.strip()}

  def download(name):
    target = install / name
    expected = checksums[name]
    if not target.exists():
      print(f"Downloading {name}", flush=True)
      temporary = target.with_suffix(".partial")
      with urllib.request.urlopen(f"{base}/{name}", timeout=120) as response, temporary.open("wb") as output:
        shutil.copyfileobj(response, output)
      temporary.replace(target)
    digest = hashlib.sha512()
    with target.open("rb") as source:
      for chunk in iter(lambda: source.read(1024 * 1024), b""):
        digest.update(chunk)
    if digest.hexdigest() != expected:
      target.unlink()
      raise ValueError(f"SHA-512 mismatch: {name}")
    return target

  system = platform.system()
  if system == "Darwin":
    data_root = Path.home() / "Library/Application Support/Godot"
    editor_suffix = "macos.universal.zip"
  elif system == "Windows":
    data_root = Path(os.environ["APPDATA"]) / "Godot"
    editor_suffix = "win64.zip"
  elif system == "Linux":
    data_root = Path(os.environ.get("XDG_DATA_HOME", str(Path.home() / ".local/share"))) / "godot"
    editor_suffix = "linux_x86_64.zip"
  else:
    raise ValueError(f"Unsupported build host: {system}")

  template_archive = download(f"Godot_v{release}_mono_export_templates.tpz")
  with zipfile.ZipFile(template_archive) as package:
    template_version = package.read("templates/version.txt").decode().strip()
  staged = install / "unpacked"
  extract(template_archive, staged)
  template_root = data_root / "export_templates" / template_version
  template_root.mkdir(parents=True, exist_ok=True)
  shutil.copytree(staged / "templates", template_root, dirs_exist_ok=True)
  shutil.rmtree(staged)
  print(f"Export templates: {template_root}", flush=True)
  if args.templates_only:
    return

  editor_root = install / "editor"
  extract(download(f"Godot_v{release}_mono_{editor_suffix}"), editor_root)
  if system == "Darwin":
    executable = next(editor_root.glob("*.app/Contents/MacOS/Godot"))
  elif system == "Windows":
    candidates = list(editor_root.rglob("*_console.exe")) or list(editor_root.rglob("Godot*.exe"))
    executable = candidates[0]
  else:
    executable = next(editor_root.rglob("Godot*_linux.x86_64"))
  if system != "Windows":
    executable.chmod(0o755)
  print(f"Godot: {executable}")
  if "GITHUB_ENV" in os.environ:
    with open(os.environ["GITHUB_ENV"], "a", encoding="utf-8") as output:
      output.write(f"GODOT_BIN={executable}\n")


if __name__ == "__main__":
  main()
