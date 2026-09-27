"""合法动作使用真实移动/旋转路径，查询与失败动作不可改变状态。"""
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from AI.engine import Engine


class PlacementTests(unittest.TestCase):
  def test_macro_matches_manual_commands(self):
    with Engine() as macro, Engine() as manual:
      for seed in range(6):
        for action in range(40):
          initial = macro.command('start', mode='endless', seed=seed)['state']
          manual.command('start', mode='endless', seed=seed)
          options = macro.command('placements')['actions']
          self.assertEqual(initial, macro.command('state')['state'])
          expected = options[action]['piece']
          result = macro.command('place', action=action)
          if expected is None:
            self.assertFalse(result['applied'])
            self.assertEqual(initial, result['state'])
            continue
          for _ in range(action // 10):
            self.assertTrue(manual.command('rotate')['applied'])
          current = manual.command('state')['state']['active']
          left = min(c['x'] for c in current['cells'])
          for _ in range(abs(action % 10 - left)):
            self.assertTrue(manual.command('move', direction='left' if action % 10 < left else 'right')['applied'])
          self.assertEqual(expected, manual.command('state')['state']['ghost'])
          manual_result = manual.command('hard_drop')
          self.assertEqual(result['state'], manual_result['state'])
          self.assertEqual(result['events'], manual_result['events'])

  def test_pause_and_resolution_have_no_legal_placements(self):
    with Engine() as engine:
      engine.command('start', mode='puzzle', level=1)
      engine.command('pause', paused=True)
      self.assertTrue(all(a['piece'] is None for a in engine.command('placements')['actions']))
      engine.command('pause', paused=False)
      result = engine.command('place', action=4)
      self.assertEqual(2100, result['state']['score'])
      self.assertEqual(1, len(result['events']))
      self.assertTrue(all(a['piece'] is None for a in engine.command('placements')['actions']))
      before = engine.command('state')['state']
      for action in (-1, 40, 'bad'):
        result = engine.exchange({'command': 'place', 'action': action})
        self.assertFalse(result['ok'])
        self.assertEqual(before, result['state'])
