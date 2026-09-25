"""游戏规则由 C# 引擎执行；Python 只发送操作并读取观察。"""

import json
from pathlib import Path
import subprocess


ROOT = Path(__file__).resolve().parents[1]
ENGINE = ROOT / "Headless/bin/Release/net8.0/ChromaDrop.Headless.dll"


class Engine:
  def __init__(self, trace=None):
    if not ENGINE.is_file():
      raise FileNotFoundError("Run python3 tools/play.py --headless --prepare-only first")
    self.trace = trace
    self.process = subprocess.Popen(
      ["dotnet", str(ENGINE)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
      text=True, bufsize=1, cwd=ROOT)

  def command(self, command, **fields):
    request = {"command": command, **fields}
    self.process.stdin.write(json.dumps(request) + "\n")
    self.process.stdin.flush()
    line = self.process.stdout.readline()
    if not line:
      raise RuntimeError(f"Engine exited unexpectedly: {self.process.poll()}")
    response = json.loads(line)
    if not response["ok"]:
      raise RuntimeError(response["error"])
    if self.trace:
      self.trace.write(json.dumps({"request": request, "response": response}) + "\n")
    return response

  def close(self):
    self.process.stdin.close()
    try:
      self.process.wait(timeout=5)
    except subprocess.TimeoutExpired:
      self.process.kill()
      self.process.wait()
    self.process.stdout.close()

  def __enter__(self):
    return self

  def __exit__(self, *args):
    self.close()
