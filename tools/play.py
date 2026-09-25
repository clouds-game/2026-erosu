"""准备 Godot C# 项目并启动本地游戏。"""

import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]


def find_godot(explicit_path: str | None) -> str:
  requested = explicit_path or os.environ.get("GODOT_BIN")
  if requested:
    resolved = shutil.which(requested)
    if resolved:
      return resolved
    path = Path(requested).expanduser()
    if path.is_file():
      return str(path)
    raise FileNotFoundError(f"找不到 Godot .NET：{requested}")

  candidates = []
  if sys.platform == "darwin":
    candidates.append(Path("/Applications/Godot_mono.app/Contents/MacOS/Godot"))
  for command in ("godot", "godot4"):
    resolved = shutil.which(command)
    if resolved:
      candidates.append(Path(resolved))
  for candidate in candidates:
    if candidate.is_file():
      return str(candidate)
  raise FileNotFoundError(
    "找不到 Godot .NET。请安装 C#/.NET 版本，或通过 --godot / GODOT_BIN 指定可执行文件。"
  )


def run(*command: str) -> None:
  subprocess.run(command, cwd=ROOT, check=True)


def main() -> None:
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument("--godot", help="Godot .NET 可执行文件路径；也可使用 GODOT_BIN")
  parser.add_argument("--demo", action="store_true", help="启动后直接载入三消示例")
  parser.add_argument("--prepare-only", action="store_true", help="只构建并导入资源，不打开窗口")
  args, game_args = parser.parse_known_args()
  if game_args[:1] == ["--"]:
    game_args.pop(0)

  godot = find_godot(args.godot)
  print("[1/2] 构建 C# 项目", flush=True)
  assets = ROOT / ".godot/mono/temp/obj/project.assets.json"
  project = ROOT / "ChromaDrop.csproj"
  if not assets.exists() or assets.stat().st_mtime < project.stat().st_mtime:
    run("dotnet", "restore", "ChromaDrop.csproj", "--nologo")
  run("dotnet", "build", "ChromaDrop.csproj", "--no-restore", "--nologo")
  print("[2/2] 导入 Godot 资源", flush=True)
  run(godot, "--headless", "--path", str(ROOT), "--editor", "--import", "--quit")

  if args.prepare_only:
    print("准备完成。")
    return

  if args.demo:
    game_args.insert(0, "--demo")
  print("启动游戏。", flush=True)
  run(godot, "--path", str(ROOT), "--", *game_args)


if __name__ == "__main__":
  try:
    main()
  except (FileNotFoundError, subprocess.CalledProcessError) as error:
    print(f"启动失败：{error}", file=sys.stderr)
    raise SystemExit(1) from error
