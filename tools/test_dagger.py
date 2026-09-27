"""DAgger 必须执行学习者动作，保留教师标签与种子隔离。"""
from pathlib import Path
import sys
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

try:
  import numpy as np
  import torch
  from AI.afterstate import CandidateRanker, candidates, dataset, load_ranker
  from AI.dagger import TASK, benchmark_policy, collect_rollouts, fingerprint, train_experiment
  from AI.engine import ENGINE
  from AI.imitation import teacher_targets
  from AI.rl_env import PlacementEnv
except ImportError:
  torch = None


@unittest.skipIf(torch is None, 'optional neural dependencies are not installed')
class DaggerTests(unittest.TestCase):
  def test_collection_executes_learner_and_labels_visited_boards(self):
    import tempfile
    torch.set_num_threads(2)
    model = CandidateRanker()
    for parameter in model.parameters():
      torch.nn.init.zeros_(parameter)
    model.eval()
    with tempfile.TemporaryDirectory() as folder:
      path = Path(folder) / 'rollout.npz'
      config = collect_rollouts(model, path, 1000, 3, set())
      self.assertEqual('learner_greedy', config['collector'])
      self.assertEqual(3, config['steps'])
      self.assertGreaterEqual(config['collected_steps'], 3)
      with np.load(path, allow_pickle=False) as data, PlacementEnv('survival') as env:
        env.reset(1000)
        for index in range(3):
          expected = teacher_targets(env.state, env.actions)
          np.testing.assert_array_equal(expected, data['targets'][index])
          first_legal = next(a['action'] for a in env.actions if a['piece'] is not None)
          self.assertEqual(first_legal, data['executed_actions'][index])
          _, _, done, _ = env.step(first_legal)
          self.assertFalse(done)
      with self.assertRaisesRegex(ValueError, 'overlap'):
        collect_rollouts(model, Path(folder) / 'invalid.npz', 1000, 3, {1000})
      self.assertFalse((Path(folder) / 'invalid.npz').exists())
      from unittest.mock import patch
      with patch('AI.dagger.fingerprint', side_effect=['original', 'changed']):
        with self.assertRaisesRegex(ValueError, 'binary changed'):
          collect_rollouts(model, Path(folder) / 'changed.npz', 1000, 3, set())
      self.assertFalse((Path(folder) / 'changed.npz').exists())

  def test_paired_training_provenance_and_overlap_rejection(self):
    import json
    import tempfile
    from types import SimpleNamespace
    torch.set_num_threads(2)
    with tempfile.TemporaryDirectory() as folder, PlacementEnv('survival') as env:
      root = Path(folder)
      for seed, name in ((1, 'train'), (2, 'validation')):
        env.reset(seed)
        boards, metadata, mask = candidates(env)
        config = {**TASK, 'engine_sha256': fingerprint(ENGINE), 'seeds': [seed]}
        np.savez_compressed(root / (name + '.npz'), boards=boards[None], metadata=metadata[None],
          mask=mask[None], targets=teacher_targets(env.state, env.actions)[None], config=json.dumps(config))
      model = CandidateRanker()
      for parameter in model.parameters():
        torch.nn.init.zeros_(parameter)
      metadata = {**TASK, 'algorithm': 'afterstate_behavior_cloning', 'training_seeds': [1],
        'validation_seeds': [2], 'engine_sha256': fingerprint(ENGINE)}
      parent = root / 'parent.pt'
      torch.save({'metadata': metadata, 'weights': model.state_dict()}, parent)
      before = fingerprint(parent)
      args = SimpleNamespace(dataset=root / 'train.npz', validation=root / 'validation.npz',
        parent=parent, directory=root / 'output', seeds=[42], rounds=1, steps=2, updates=1,
        collection_seed=1000)
      train_experiment(args)
      manifest = json.loads((args.directory / 'experiment.json').read_text())
      self.assertTrue(manifest['complete'])
      self.assertEqual(before, fingerprint(parent))
      for arm in ('teacher_replay', 'dagger'):
        _, trained = load_ranker(Path(manifest['models'][arm + '-42']['path']))
        self.assertEqual(1, trained['fine_tuning_updates'])
        self.assertIn(1, trained['training_seeds'])
        self.assertIn(1000, trained['training_seeds'])
        self.assertEqual([2], trained['validation_seeds'])
        with self.assertRaisesRegex(ValueError, 'overlap'):
          benchmark_policy(Path(manifest['models'][arm + '-42']['path']), [2])
      with self.assertRaises(FileExistsError):
        train_experiment(args)
      from AI.dagger import evaluate_experiment
      from unittest.mock import patch
      evaluation = SimpleNamespace(directory=args.directory, seed=1, episodes=1, report=root / 'report.json')
      with patch('AI.dagger.benchmark_policy') as benchmark:
        with self.assertRaisesRegex(ValueError, 'overlap'):
          evaluate_experiment(evaluation)
        benchmark.assert_not_called()
      self.assertFalse(evaluation.report.exists())
      manifest_path = args.directory / 'experiment.json'
      before_manifest = manifest_path.read_bytes()
      evaluation.report = manifest_path
      with self.assertRaisesRegex(ValueError, 'overwrite'):
        evaluate_experiment(evaluation)
      self.assertEqual(before_manifest, manifest_path.read_bytes())


if __name__ == '__main__':
  unittest.main()
