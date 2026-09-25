"""Python 策略与真实引擎对接的回归测试。"""

from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from AI.engine import Engine
from AI.policy import BASELINE, candidates
from AI.train import episode


class AiTests(unittest.TestCase):
  def test_replay_is_deterministic_and_has_real_progress(self):
    with Engine() as engine:
      first = episode(engine, 42, BASELINE, 20)
      second = episode(engine, 42, BASELINE, 20)
    self.assertEqual(first, second)
    self.assertEqual(20, first["locked"])
    self.assertTrue(first["truncated"])

  def test_candidates_match_shared_engine_landings_on_empty_board(self):
    with Engine() as engine:
      state = engine.command("start", mode="endless", seed=42)["state"]
      options = list(candidates(state))
      self.assertTrue(options)
      for rotation, target, features in options:
        state = engine.command("start", mode="endless", seed=42)["state"]
        for _ in range(rotation):
          state = engine.command("rotate")["state"]
        left = min(cell["x"] for cell in state["active"]["cells"])
        for _ in range(abs(target - left)):
          result = engine.command("move", direction="left" if target < left else "right")
          self.assertTrue(result["applied"])
          state = result["state"]
        ghost = state["ghost"]["cells"]
        self.assertEqual(target, min(cell["x"] for cell in ghost))
        self.assertEqual(features[3], state["height"] - min(cell["y"] for cell in ghost))

  def test_color_feature_counts_whole_blocks(self):
    with Engine() as engine:
      state = engine.command("start", mode="puzzle", level=1)["state"]
    # O at x=4 touches two cells of the same existing O: one neighbor, not two.
    option = next(option for option in candidates(state) if option[:2] == (0, 4))
    self.assertEqual(1, option[2][4])


if __name__ == "__main__":
  unittest.main()
