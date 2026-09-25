"""游戏规则由 C# 引擎执行；Python 只发送操作并读取观察。"""

import json
from pathlib import Path
import subprocess
from .trajectory import Recorder


ROOT = Path(__file__).resolve().parents[1]
ENGINE = ROOT / "Headless/bin/Release/net8.0/ChromaDrop.Headless.dll"


class Engine:
  def __init__(self, trace=None):
    if not ENGINE.is_file():
      raise FileNotFoundError("Run python3 tools/play.py --headless --prepare-only first")
    self.trace = Recorder(trace, ENGINE) if trace is not None else None
    self.process = subprocess.Popen(
      ["dotnet", str(ENGINE)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
      text=True, bufsize=1, cwd=ROOT)

  def command(self, command, **fields):
    request = {"command": command, **fields}
    response = self.exchange(request)
    if not response["ok"]:
      raise RuntimeError(response["error"])
    return response

  def exchange(self, request):
    self.process.stdin.write(json.dumps(request) + "\n")
    self.process.stdin.flush()
    line = self.process.stdout.readline()
    if not line:
      raise RuntimeError(f"Engine exited unexpectedly: {self.process.poll()}")
    response = json.loads(line)
    if self.trace:
      self.trace.record(request, response)
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
    try:
      if self.trace and args[0] is None:
        self.trace.finish()
    finally:
      self.close()
