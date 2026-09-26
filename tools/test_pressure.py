"""生存统计不得把预算截断误报成死亡或通关。"""

from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from AI.pressure import distribution


class PressureTests(unittest.TestCase):
  def test_reaching_budget_and_surviving_budget_are_distinct(self):
    runs = [{"locked": locked, "truncated": truncated, "score": 1000,
      "rarity_points": 0, "cleared_by_color": [0] * 7, "rejected_actions": 0}
      for locked, truncated in [(20, False), (100, False), (300, False), (300, True)]]
    result = distribution(runs, 300)
    self.assertEqual(3, result["terminated"])
    self.assertEqual(1, result["truncated"])
    self.assertEqual(0.5, result["reached"]["300"])
    self.assertEqual(200, result["median_locked_capped"])
    self.assertEqual(20, result["p10_locked_capped"])
    self.assertEqual(300, result["p90_locked_capped"])
    self.assertNotIn("300", distribution(runs[:2], 150)["reached"])
