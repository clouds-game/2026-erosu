"""多模式演示应复现实际评估，并在录制前拒绝不匹配或污染的输入。"""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
try:
  import torch
  import numpy
except ImportError:
  torch = None

if torch is not None:
  from AI.engine import ENGINE
  from AI.modes import ALL_MODES
  from AI.multi_mode_demo import build_demo, fingerprint
  from AI.neural import ActorCritic
  from AI.ppo import summarize, tensors
  from AI.rl_env import PlacementEnv
  from AI.trajectory import read


@unittest.skipIf(torch is None, 'Install AI/requirements-rl.txt for neural tests')
class MultiModeDemoTests(unittest.TestCase):
  @classmethod
  def setUpClass(cls):
    torch.set_num_threads(1)
    cls.folder = tempfile.TemporaryDirectory()
    cls.root = Path(cls.folder.name)
    cls.model_path = cls.root / 'policy.pt'
    cls.metadata = {'observation_version': 2, 'objective': 'survival', 'max_pieces': 3,
      'color_profile': 'mixed', 'game_modes': [mode.options() for mode in ALL_MODES],
      'training_seeds': [1], 'validation_seeds': [2], 'engine_sha256': fingerprint(ENGINE),
      'color_conditioned': True}
    torch.manual_seed(11)
    model = ActorCritic(2, color_conditioned=True).eval()
    cls.weights = model.state_dict()
    torch.save({'metadata': cls.metadata, 'weights': cls.weights}, cls.model_path)
    runs = []
    with PlacementEnv('survival', 3, observation_version=2) as env:
      for mode in ALL_MODES:
        observation = env.reset(12000000, mode)
        while True:
          with torch.no_grad():
            distribution, _ = model(*tensors([observation]))
          observation, _, done, result = env.step(distribution.logits.argmax(-1).item())
          if done:
            runs.append(result)
            break
    evaluated = {'model_sha256': fingerprint(cls.model_path), 'runs': runs, 'summary': summarize(runs)}
    cls.report = {'complete': True, 'action_selection': 'greedy', 'engine_sha256': fingerprint(ENGINE),
      'objective': 'survival', 'max_pieces': 3, 'game_modes': cls.metadata['game_modes'],
      'test': {'seeds': [12000000], 'baseline': evaluated, 'selected': copy.deepcopy(evaluated)}}

  @classmethod
  def tearDownClass(cls):
    cls.folder.cleanup()

  def setUp(self):
    self.folder = tempfile.TemporaryDirectory(dir=self.root)
    self.addCleanup(self.folder.cleanup)
    self.path = Path(self.folder.name)

  def generate(self, report=None, model_path=None, seeds=None):
    report_path = self.path / 'report.json'
    report_path.write_text(json.dumps(report or self.report))
    return build_demo(report_path, model_path or self.model_path, model_path or self.model_path,
      self.path / 'demo', seeds)

  def test_all_modes_render_actual_verified_episodes(self):
    summary = self.generate()
    self.assertEqual([mode.key for mode in ALL_MODES], [s['game_mode'] for s in summary['scenarios']])
    self.assertEqual(12, summary['benchmark_episodes'])
    self.assertEqual('capped', summary['benchmark_metric'])
    for mode, scenario in zip(ALL_MODES, summary['scenarios']):
      self.assertIn('seed 12000000', scenario['label'])
      for policy in scenario['policies']:
        result = policy['result']
        self.assertEqual(mode.options(), {key: result[key] for key in mode.options()})
        self.assertEqual(3, result['locked'])
        self.assertTrue(result['capped'])
        self.assertGreater(policy['verified_steps'], 3)
        _, steps = read(self.path / 'demo' / f'{mode.key}-12000000-{policy["key"]}.jsonl')
        initial = steps[0]['response']['state']
        self.assertEqual(3 if mode.anchored_blocks else 0,
          sum(piece['anchored'] for piece in initial['pieces']))
    html = (self.path / 'demo' / 'index.html').read_text()
    self.assertNotIn('__REPLAY_DATA__', html)
    self.assertIn('rare_seven_anchored_fill', html)
    self.assertIn('"anchored": true', html)

  def test_rejects_invalid_report_and_nonheldout_seed_before_recording(self):
    for field, value in [('engine_sha256', 'wrong'), ('complete', False), ('action_selection', 'sample')]:
      with self.subTest(field=field), patch('AI.multi_mode_demo.record_policy') as record:
        report = {**self.report, field: value}
        with self.assertRaises(ValueError):
          self.generate(report)
        record.assert_not_called()
    for seeds in ([123], [], [12000000, 12000000]):
      with self.subTest(seeds=seeds), patch('AI.multi_mode_demo.record_policy') as record:
        with self.assertRaises(ValueError):
          self.generate(seeds=seeds)
        record.assert_not_called()

  def test_rejects_checkpoint_task_flags_and_leakage_before_recording(self):
    for field, value in [('objective', 'score'), ('max_pieces', 4),
        ('game_modes', [ALL_MODES[0].options()]), ('engine_sha256', 'wrong'), ('observation_version', 1),
        ('training_seeds', [12000000]), ('validation_seeds', [12000000])]:
      with self.subTest(field=field), patch('AI.multi_mode_demo.record_policy') as record:
        changed = self.path / 'changed.pt'
        weights = ActorCritic(1, color_conditioned=True).state_dict() if field == 'observation_version' else self.weights
        torch.save({'metadata': {**self.metadata, field: value}, 'weights': weights}, changed)
        report = copy.deepcopy(self.report)
        for key in ('baseline', 'selected'):
          report['test'][key]['model_sha256'] = fingerprint(changed)
        with self.assertRaises(ValueError):
          self.generate(report, changed)
        record.assert_not_called()

  def test_rejects_checkpoint_hash_and_does_not_overwrite_demo(self):
    report = copy.deepcopy(self.report)
    report['test']['baseline']['model_sha256'] = 'wrong'
    with patch('AI.multi_mode_demo.record_policy') as record:
      with self.assertRaises(ValueError):
        self.generate(report)
      output = self.path / 'demo'
      output.mkdir()
      existing = output / 'keep.txt'
      existing.write_text('keep')
      with self.assertRaises(FileExistsError):
        self.generate()
      self.assertEqual('keep', existing.read_text())
      record.assert_not_called()

  def test_rejects_episode_that_does_not_reproduce_report(self):
    report = copy.deepcopy(self.report)
    report['test']['baseline']['runs'][0]['score'] += 1
    with self.assertRaisesRegex(ValueError, 'diverges.*score'):
      self.generate(report)
    self.assertFalse((self.path / 'demo' / 'index.html').exists())


if __name__ == '__main__':
  unittest.main()
