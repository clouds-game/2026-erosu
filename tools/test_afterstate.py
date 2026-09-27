"""候选模拟必须复用真实结算，不改变状态或随机序列。"""
from pathlib import Path
import sys
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from AI.engine import Engine


class AfterstateTests(unittest.TestCase):
  def test_preview_matches_resolution_and_preserves_rng(self):
    with Engine() as engine, Engine() as control:
      for mode in ({'mode': 'endless', 'seed': 71}, {'mode': 'puzzle', 'level': 1}, {'mode': 'puzzle', 'level': 3}):
        state = engine.command('start', **mode)['state']
        control.command('start', **mode)
        for decision in range(15):
          before = engine.command('state')
          options = engine.command('afterstates')['actions']
          self.assertEqual(options, engine.command('afterstates')['actions'])
          self.assertEqual(before, engine.command('state'))
          legal = [a for a in options if a['state'] is not None]
          if not legal:
            break
          # Puzzle 1 action 4 explicitly triggers a scoring clear.
          chosen = options[4] if mode.get('level') == 1 and decision == 0 else legal[decision % len(legal)]
          for game in (engine, control):
            actual = game.command('place', action=chosen['action'])['state']
            for _ in range(3600):
              if actual['phase'] not in ('clearing', 'settling'):
                break
              actual = game.command('tick', count=1)['state']
          self.assertEqual(engine.command('state'), control.command('state'))
          preview = chosen['state']
          for key in ('pieces', 'score', 'cleared', 'locked', 'best_chain', 'active', 'phase', 'is_finished'):
            self.assertEqual(actual[key], preview[key], key)
          if mode['mode'] == 'endless':
            self.assertEqual(state['next'][1:], preview['next'])
          state = actual
          if state['is_finished']:
            break

  def test_paused_preview_is_invalid(self):
    with Engine() as engine:
      engine.command('start', mode='endless', seed=8)
      engine.command('pause', paused=True)
      self.assertTrue(all(a['state'] is None for a in engine.command('afterstates')['actions']))

  def test_candidate_mask_and_gradients(self):
    try:
      import torch
      from AI.afterstate import CandidateRanker, candidates
      from AI.rl_env import PlacementEnv
    except ImportError:
      self.skipTest('optional neural dependencies are not installed')
    torch.set_num_threads(2)
    with PlacementEnv('survival') as env:
      env.reset(91)
      data = tuple(torch.from_numpy(x)[None] for x in candidates(env))
      model = CandidateRanker()
      scores = model(*data)
      self.assertEqual((1, 40), tuple(scores.shape))
      self.assertTrue((scores[~data[2]] == -1e9).all())
      permutation = torch.randperm(40)
      permuted = model(*(t[:, permutation] for t in data))
      torch.testing.assert_close(permuted, scores[:, permutation])
      scores[data[2]].sum().backward()
      self.assertTrue(all(p.grad is not None and p.grad.isfinite().all() for p in model.parameters()))

  def test_training_checkpoint_and_episode_split(self):
    try:
      import torch
      import numpy as np
      from AI.afterstate import candidates, train, load_ranker, VERSION
      from AI.imitation import teacher_targets
      from AI.rl_env import PlacementEnv
    except ImportError:
      self.skipTest('optional neural dependencies are not installed')
    import json
    import tempfile
    from types import SimpleNamespace
    torch.set_num_threads(2)
    with tempfile.TemporaryDirectory() as folder, PlacementEnv('survival') as env:
      root = Path(folder)
      for seed, name in ((1, 'train'), (2, 'validation')):
        env.reset(seed)
        boards, meta, mask = candidates(env)
        target = teacher_targets(env.state, env.actions)
        config = {'version': VERSION, 'seeds': [seed], 'engine_sha256': 'test',
          'objective': 'survival', 'max_pieces': 300, 'color_profile': 'rare_seven'}
        np.savez_compressed(root / (name + '.npz'), boards=boards[None], metadata=meta[None],
          mask=mask[None], targets=target[None], config=json.dumps(config))
      args = SimpleNamespace(dataset=root / 'train.npz', validation=root / 'validation.npz',
        model=root / 'model.pt', seed=42, epochs=1)
      train(args)
      model, metadata = load_ranker(args.model)
      self.assertEqual([1], metadata['training_seeds'])
      self.assertEqual([2], metadata['validation_seeds'])
      self.assertFalse(model.training)
      args.validation = args.dataset
      with self.assertRaisesRegex(ValueError, 'overlap'):
        train(args)


if __name__ == '__main__':
  unittest.main()
