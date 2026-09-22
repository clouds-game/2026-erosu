"""发布固定地址的 nightly 预发布；仅在两个桌面包校验通过后更新。"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess


def gh(*args):
  return subprocess.run(["gh", *args], check=True, capture_output=True, text=True).stdout


def verify_packages(directory):
  assets = []
  for platform in ["windows-x64", "macos-universal"]:
    archive = directory / f"chroma-drop-{platform}.zip"
    checksum = archive.with_suffix(".zip.sha256")
    expected, filename = checksum.read_text().strip().split()
    if filename != archive.name or hashlib.sha256(archive.read_bytes()).hexdigest() != expected:
      raise ValueError(f"Invalid package checksum: {archive.name}")
    assets.extend([str(archive), str(checksum)])
  return assets


def main():
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument("directory", type=Path)
  args = parser.parse_args()
  assets = verify_packages(args.directory)
  repository = os.environ["GITHUB_REPOSITORY"]
  sha = os.environ["GITHUB_SHA"]
  run_number = int(os.environ["GITHUB_RUN_NUMBER"])
  run_id = int(os.environ["GITHUB_RUN_ID"])
  if not re.fullmatch(r"[0-9a-f]{40}", sha):
    raise ValueError("Expected the full build commit SHA")
  endpoint = f"repos/{repository}"
  response = subprocess.run(["gh", "api", f"{endpoint}/releases/tags/nightly"], capture_output=True, text=True)
  if response.returncode and "HTTP 404" not in response.stderr:
    raise RuntimeError(response.stderr)
  existing = json.loads(response.stdout) if response.returncode == 0 else None
  if existing:
    previous = re.search(r"<!-- nightly-run: (\d+) -->", existing.get("body") or "")
    if previous and int(previous[1]) > run_number:
      print("A newer successful build is already published; skipping this older rerun.")
      return

  timestamp = datetime.now(timezone.utc).isoformat(timespec="seconds")
  metadata = args.directory / "build-info.json"
  metadata.write_text(json.dumps({"commit": sha, "run_number": run_number, "run_id": run_id,
    "built_at": timestamp, "repository": repository}, indent=2) + "\n")
  assets.append(str(metadata))
  notes = args.directory / "nightly-notes.md"
  instructions = (Path(__file__).resolve().parents[1] / "docs/发布说明.md").read_text()
  notes.write_text(f"# Nightly · 最新成功构建\n\n"
    f"提交：[{sha[:7]}](https://github.com/{repository}/commit/{sha})\n\n"
    f"构建：[{run_number}](https://github.com/{repository}/actions/runs/{run_id}) · {timestamp}\n\n"
    f"固定下载地址，每次成功构建更新。此版本为滚动预发布，不替代稳定版本。\n\n"
    f"{instructions}\n<!-- nightly-run: {run_number} -->\n", encoding="utf-8")
  if existing:
    gh("release", "upload", "nightly", *assets, "--clobber", "--repo", repository)
    gh("api", "--method", "PATCH", f"{endpoint}/git/refs/tags/nightly", "-f", f"sha={sha}", "-F", "force=true")
    gh("release", "edit", "nightly", "--title", "Nightly · latest build", "--prerelease", "--latest=false",
      "--notes-file", str(notes), "--repo", repository)
  else:
    gh("release", "create", "nightly", *assets, "--target", sha, "--title", "Nightly · latest build",
      "--prerelease", "--latest=false", "--notes-file", str(notes), "--repo", repository)
  print(f"Published https://github.com/{repository}/releases/tag/nightly")


if __name__ == "__main__":
  main()
