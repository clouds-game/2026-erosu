import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from AI.engine import Engine, ENGINE
from AI.trajectory import Recorder, read, replay


class TrajectoryTests(unittest.TestCase):
  def setUp(self):
    self.temp = tempfile.TemporaryDirectory()
    self.addCleanup(self.temp.cleanup)
    self.path = Path(self.temp.name) / "game.jsonl"

  def record(self):
    with self.path.open("w") as stream, Engine(stream) as engine:
      engine.command("start", mode="puzzle", level=1)
      engine.command("hard_drop")
      engine.exchange({"command": "tick", "count": -1})
      engine.command("tick", count=120)
      engine.command("start", mode="endless", seed=42)
      engine.command("pause", paused=True)
      engine.command("tick", count=60)

  def test_roundtrip_including_error_reset_and_pause(self):
    self.record()
    result = replay(self.path)
    self.assertTrue(result["verified"])
    self.assertEqual(7, result["steps"])
    self.assertTrue(result["state"]["paused"])

  def test_truncation_and_corruption_are_rejected(self):
    self.record()
    original = self.path.read_text().splitlines()
    self.path.write_text("\n".join(original[:-1]) + "\n")
    with self.assertRaisesRegex(ValueError, "Incomplete"):
      read(self.path)
    altered = json.loads(original[1])
    altered["request"]["level"] = 2
    original[1] = json.dumps(altered)
    self.path.write_text("\n".join(original) + "\n")
    with self.assertRaisesRegex(ValueError, "checksum"):
      read(self.path)

  def test_semantic_divergence_reports_step_and_field(self):
    self.record()
    _, steps = read(self.path)
    steps[0]["response"]["state"]["score"] = 999
    with self.path.open("w") as stream:
      recorder = Recorder(stream, ENGINE)
      for step in steps:
        recorder.record(step["request"], step["response"])
      recorder.finish()
    with self.assertRaisesRegex(ValueError, "step 0: response.state.score"):
      replay(self.path)

  def test_interrupted_recording_has_no_completion_marker(self):
    with self.assertRaises(RuntimeError):
      with self.path.open("w") as stream, Engine(stream) as engine:
        engine.command("start", mode="endless", seed=42)
        raise RuntimeError("interrupted")
    with self.assertRaises(ValueError):
      read(self.path)


if __name__ == "__main__":
  unittest.main()
