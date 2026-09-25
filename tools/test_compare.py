from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from AI.compare import compare, frames
from AI.engine import ROOT
from AI.trajectory import read


class ComparisonTests(unittest.TestCase):
  def test_same_seed_verified_and_clear_frames_stay_in_turn(self):
    with tempfile.TemporaryDirectory() as folder:
      path = Path(folder)
      summary = compare(ROOT / 'AI/models/starter.json', 3000100, 15, path)
      self.assertEqual(2, len(summary['policies']))
      for filename in ['before.jsonl', 'after.jsonl']:
        _, steps = read(path / filename)
        groups = frames(steps)
        self.assertEqual(15, len(groups))
        for index, group in enumerate(groups, 1):
          self.assertEqual(index, group[-1]['locked'])
          self.assertNotIn(group[-1]['phase'], ('clearing', 'settling'))
          for state in group:
            if state['phase'] in ('clearing', 'settling'):
              self.assertEqual(index, state['locked'])
      html = (path / 'index.html').read_text()
      self.assertNotIn('__REPLAY_DATA__', html)
      self.assertIn('3000100', html)
