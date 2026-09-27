"""神经策略演示只渲染真实轨迹，查询快照不作为玩家动作。"""
from pathlib import Path
import sys
import tempfile
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from AI.compare import frames, write_viewer
from AI.trajectory import read


class NeuralComparisonTests(unittest.TestCase):
  def test_recorded_teacher_macro_keeps_turn_boundaries(self):
    try:
      import torch
      from AI.neural_compare import record_policy
    except ImportError:
      self.skipTest('optional neural dependencies are not installed')
    torch.set_num_threads(2)
    with tempfile.TemporaryDirectory() as folder:
      path = Path(folder) / 'game.jsonl'
      policy, sequence, colors = record_policy(None, 9000000, path, 3)
      self.assertEqual(3, policy['result']['locked'])
      self.assertTrue(policy['result']['capped'])
      self.assertEqual(3, len(policy['turns']))
      self.assertEqual(7, len(colors))
      self.assertGreaterEqual(len(sequence), 3)
      _, steps = read(path)
      actual = [s for s in steps if s['request']['command'] not in ('placements', 'afterstates', 'state')]
      self.assertEqual(frames(actual), policy['turns'])
      for index, group in enumerate(policy['turns'], 1):
        self.assertEqual(index, group[-1]['locked'])
        self.assertNotIn(group[-1]['phase'], ('clearing', 'settling'))

  def test_self_contained_viewer_escapes_data(self):
    with tempfile.TemporaryDirectory() as folder:
      path = Path(folder) / 'index.html'
      write_viewer(path, {'title': '</script><script>bad()</script>'})
      html = path.read_text()
      self.assertNotIn('__REPLAY_DATA__', html)
      self.assertNotIn('__REPLAY_SCRIPT__', html)
      self.assertNotIn('</script><script>bad()', html)
      self.assertIn('\\u003c/script>', html)


if __name__ == '__main__':
  unittest.main()
