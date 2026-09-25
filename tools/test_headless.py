"""通过真实 JSON 管道验证无界面引擎。先构建 Headless 的 Release 配置。"""

import json
from pathlib import Path
import subprocess
import sys
import time
import unittest


ROOT = Path(__file__).resolve().parents[1]
ENGINE = ROOT / "Headless/bin/Release/net8.0/ChromaDrop.Headless.dll"


class HeadlessTests(unittest.TestCase):
  def setUp(self):
    self.assertTrue(ENGINE.is_file(), "Build Headless/ChromaDrop.Headless.csproj -c Release first")
    self.process = subprocess.Popen(
      ["dotnet", str(ENGINE)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
      stderr=subprocess.PIPE, text=True, cwd=ROOT)
    self.addCleanup(self.close)

  def close(self):
    self.process.stdin.close()
    self.process.wait(timeout=10)
    self.process.stdout.close()
    self.process.stderr.close()

  def send(self, command):
    self.process.stdin.write(json.dumps(command) + "\n")
    self.process.stdin.flush()
    return json.loads(self.process.stdout.readline())

  def start(self):
    return self.send({"command": "start", "mode": "endless", "seed": 42})

  def test_external_time_and_batch_equivalence(self):
    initial = self.start()
    time.sleep(0.1)
    self.assertEqual(initial, self.send({"command": "state"}))
    batched = self.send({"command": "tick", "count": 120})
    self.assertNotEqual(initial["state"]["active"], batched["state"]["active"])
    self.start()
    for _ in range(120):
      single = self.send({"command": "tick", "count": 1})
    self.assertEqual(batched, single)

  def test_pause_and_invalid_requests_preserve_state(self):
    self.start()
    paused = self.send({"command": "pause", "paused": True})
    result = self.send({"command": "tick", "count": 120})
    self.assertEqual(paused["state"], result["state"])
    for command in [
      {"command": "tick", "count": -1},
      {"command": "tick", "count": 3601},
      {"command": "tick", "count": 1, "soft_drop": "yes"},
      {"command": "move", "direction": "teleport"},
      {"command": "start", "mode": "puzzle", "level": 999},
      {"command": "tick", "count": 1, "typo": True},
    ]:
      error = self.send(command)
      self.assertFalse(error["ok"])
      self.assertEqual(result["state"], error["state"])
      self.assertEqual(result["ticks"], error["ticks"])

  def test_puzzle_clear_events_and_completion(self):
    self.assertFalse(self.send({"command": "state"})["ok"])
    result = self.send({"command": "start", "mode": "puzzle", "level": 1, "id": "puzzle"})
    self.assertEqual("puzzle", result["id"])
    result = self.send({"command": "hard_drop"})
    self.assertEqual(1, len(result["events"]))
    self.assertEqual(3, len(result["events"][0]["pieces"]))
    self.assertEqual("clearing", result["state"]["phase"])
    result = self.send({"command": "tick", "count": 120})
    self.assertEqual("won", result["state"]["phase"])

  def test_malformed_json_recovers_and_eof_exits(self):
    self.process.stdin.write('{broken\n')
    self.process.stdin.flush()
    self.assertFalse(json.loads(self.process.stdout.readline())["ok"])
    self.assertTrue(self.start()["ok"])
    self.process.stdin.close()
    self.assertEqual(0, self.process.wait(timeout=10))

  def test_batch_preserves_all_chain_events(self):
    result = self.send({"command": "start", "mode": "puzzle", "level": 4})
    events = []
    for target, rotations in [(0, 0), (1, 0), (5, 0), (2, 0), (0, 1)]:
      for _ in range(rotations):
        result = self.send({"command": "rotate"})
      left = min(cell["x"] for cell in result["state"]["active"]["cells"])
      for _ in range(abs(target - left)):
        result = self.send({"command": "move", "direction": "left" if target < left else "right"})
        self.assertTrue(result["applied"])
      result = self.send({"command": "hard_drop"})
      events.extend(result["events"])
      result = self.send({"command": "tick", "count": 600})
      events.extend(result["events"])
    self.assertEqual("won", result["state"]["phase"])
    self.assertEqual([1, 2, 3], [event["chain"] for event in events])
    self.assertEqual(20900, result["state"]["score"])


class LauncherTests(unittest.TestCase):
  def test_launcher_keeps_build_logs_off_protocol_and_preserves_input(self):
    commands = [
      {"command": "start", "mode": "endless", "seed": 42},
      {"command": "tick", "count": 60},
    ]
    result = subprocess.run(
      [sys.executable, str(ROOT / "tools/play.py"), "--headless"],
      input="".join(json.dumps(command) + "\n" for command in commands),
      capture_output=True, text=True, timeout=120, cwd=ROOT.parent)
    self.assertEqual(0, result.returncode, result.stderr)
    replies = [json.loads(line) for line in result.stdout.splitlines()]
    self.assertEqual(2, len(replies))
    self.assertTrue(all(reply["ok"] for reply in replies))
    self.assertEqual(60, replies[1]["ticks"])


if __name__ == "__main__":
  unittest.main()
