"""教师只标记合法动作；训练/验证按整局隔离，迁移保留数据血缘。"""
from pathlib import Path
from types import SimpleNamespace
import copy
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
try:
  import torch
  import numpy as np
except ImportError:
  torch = None

if torch is not None:
  from AI.imitation import teacher_targets, collect, read_dataset, clone
  from AI.ppo import train, load_model, evaluate
  from AI.rl_env import PlacementEnv


@unittest.skipIf(torch is None, 'Install AI/requirements-rl.txt')
class ImitationTests(unittest.TestCase):
  def test_teacher_labels_only_reachable_actions_and_treats_duplicates_equally(self):
    with PlacementEnv('survival') as env:
      env.reset(42)
      targets = teacher_targets(env.state, env.actions)
      self.assertEqual('float32', str(targets.dtype))
      self.assertAlmostEqual(1, float(targets.sum()))
      for option in env.actions:
        if option['piece'] is None:
          self.assertEqual(0, targets[option['action']])
      best = int(targets.argmax())
      actions = copy.deepcopy(env.actions)
      # Keep only one teacher-selected landing and an identical copy in another slot.
      duplicate = (best + 1) % 40
      for option in actions:
        option['piece'] = None
      actions[best]['piece'] = copy.deepcopy(env.actions[best]['piece'])
      actions[duplicate]['piece'] = copy.deepcopy(env.actions[best]['piece'])
      targets = teacher_targets(env.state, actions)
      self.assertEqual(0.5, targets[best])
      self.assertEqual(0.5, targets[duplicate])
      env.step(best)

  def test_dataset_clone_transfer_and_seed_isolation(self):
    torch.set_num_threads(1)
    with tempfile.TemporaryDirectory() as directory:
      root = Path(directory)
      dataset, validation = root / 'train.npz', root / 'validation.npz'
      collect(dataset, 1000000, 4, max_pieces=4)
      collect(validation, 2000000, 4, max_pieces=4)
      config, data = read_dataset(dataset)
      self.assertEqual(4, len(data[0]))
      self.assertEqual(4, config['episodes'][0]['locked'])
      cloning = SimpleNamespace(dataset=dataset, validation=validation, seed=42, epochs=1, model=root/'bc.pt')
      clone(cloning)
      _, parent = load_model(cloning.model)
      self.assertEqual([1000000], parent['training_seeds'])
      self.assertEqual([2000000], parent['validation_seeds'])
      args = SimpleNamespace(init_model=cloning.model, model=root/'ppo.pt', objective='survival',
        max_pieces=4, color_profile='rare_seven', seed=42, steps=4, rollout=4)
      train(args)
      _, metadata = load_model(args.model)
      self.assertIn(1000000, metadata['training_seeds'])
      self.assertEqual([2000000], metadata['validation_seeds'])
      self.assertIsNotNone(metadata['initialization_sha256'])
      for seed in (1000000, 2000000):
        with self.assertRaisesRegex(ValueError, 'overlap'):
          evaluate(SimpleNamespace(model=args.model, seed=seed, episodes=1))
      args.objective = 'score'
      with self.assertRaisesRegex(ValueError, 'mismatch'):
        train(args)
      cloning.validation = dataset
      with self.assertRaisesRegex(ValueError, 'overlap'):
        clone(cloning)

  def test_benchmark_records_actual_clear_counts(self):
    from AI.imitation_benchmark import benchmark_policy
    result = benchmark_policy(None, 'random', [5100000])
    run = result['runs'][0]
    self.assertGreater(run['locked'], 0)
    self.assertGreaterEqual(run['gold_cleared'], 0)
    self.assertLessEqual(run['gold_cleared'], run['cleared'])
    self.assertEqual(run['cleared'] / run['locked'], result['summary']['clears_per_placed_piece'])
