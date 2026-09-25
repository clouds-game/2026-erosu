"""构建并启动 Godot 游戏或无界面 JSON 引擎。"""

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
  parser.add_argument("--headless", action="store_true", help="运行外部 tick 驱动的 JSON 引擎，无需 Godot")
  trajectory = parser.add_mutually_exclusive_group()
  trajectory.add_argument("--record", type=Path, help="录制 JSON 轨迹到新文件（隐含 --headless）")
  trajectory.add_argument("--replay", type=Path, help="验证并回放 JSON 轨迹（隐含 --headless）")
  parser.add_argument("--render", action="store_true", help="终端显示回放棋盘")
  parser.add_argument("--step", action="store_true", help="回放时按 Enter 单步推进")
  parser.add_argument("--godot", help="Godot .NET 可执行文件路径；也可使用 GODOT_BIN")
  parser.add_argument("--demo", action="store_true", help="启动后直接载入三消示例")
  parser.add_argument("--prepare-only", action="store_true", help="只准备构建及所需资源，不启动")
  args, game_args = parser.parse_known_args()
  if game_args[:1] == ["--"]:
    game_args.pop(0)

  if (args.render or args.step) and not args.replay:
    parser.error("--render / --step 需要 --replay")
  if (args.record or args.replay) and args.prepare_only:
    parser.error("轨迹录制/回放不能与 --prepare-only 同用")
  if args.headless or args.record or args.replay:
    if args.godot or args.demo or game_args:
      parser.error("--headless 不接受 --godot、--demo 或游戏参数；请通过 JSON 控制对局。")
    print("构建无界面引擎。", file=sys.stderr, flush=True)
    subprocess.run(
      ["dotnet", "build", "Headless/ChromaDrop.Headless.csproj", "-c", "Release", "--nologo"],
      cwd=ROOT, check=True, stdin=subprocess.DEVNULL, stdout=sys.stderr)
    if not args.prepare_only:
      if args.record or args.replay:
        command = [sys.executable, "-m", "AI.trajectory", "record" if args.record else "replay",
          str((args.record or args.replay).resolve())]
        if args.render:
          command.append("--render")
        if args.step:
          command.append("--step")
        run(*command)
      else:
        run("dotnet", str(ROOT / "Headless/bin/Release/net8.0/ChromaDrop.Headless.dll"))
    return

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
