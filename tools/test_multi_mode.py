"""特征模式观察、旧策略迁移与跨十二种模式的真实引擎回归。"""
from pathlib import Path
import copy
import sys
import tempfile
from types import SimpleNamespace
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
try:
  import numpy as np
  import torch
except ImportError:
  torch = None

if torch is not None:
  from AI.modes import ALL_MODES, GameMode
  from AI.neural import ActorCritic, FEATURE_OBS_VERSION, encode, upgrade
  from AI.ppo import checkpoint_modes, tensors, train, load_model
  from AI.rl_env import PlacementEnv


@unittest.skipIf(torch is None, 'Install AI/requirements-rl.txt for neural tests')
class MultiModeTests(unittest.TestCase):
  def test_rule_identity_history_and_forecast_order_are_observable(self):
    with PlacementEnv('survival', observation_version=2) as env:
      env.reset(42, GameMode('rare_seven', True, True))
      state = copy.deepcopy(env.state)
      state['known_enclosed_cells'] = [{'x': 2, 'y': 16}]
      board, meta = encode(state, 'survival', 1, 2)
      self.assertEqual((11, 18, 10), board.shape)
      self.assertEqual(189, len(meta))
      self.assertEqual(3, board[9].sum())
      self.assertEqual(1, board[10, 16, 2])
      np.testing.assert_array_equal(meta[73:75], [1, 1])
      anchor = {'shape': 'single', 'color': 6, 'anchored': True, 'cells': [{'x': 0, 'y': 0}]}
      state['forecast'] = [*state['next'], anchor]
      tail = encode(state, 'survival', 1, 2)[1]
      state['forecast'] = [anchor, *state['next']]
      head = encode(state, 'survival', 1, 2)[1]
      self.assertFalse(np.array_equal(tail, head))
      self.assertEqual(1, tail[75 + 3 * 16 + 15])
      state['pieces'][0]['anchored'] = False
      self.assertEqual(2, encode(state, 'survival', 1, 2)[0][9].sum())
      del state['known_enclosed_cells']
      with self.assertRaises(ValueError):
        encode(state, 'survival', 1, 2)

  def test_upgrade_preserves_original_policy_and_value(self):
    torch.set_num_threads(1)
    torch.manual_seed(12)
    original = ActorCritic()
    expanded = upgrade(original)
    with PlacementEnv('survival', observation_version=2) as env:
      current = env.reset(42, GameMode('rare_seven', True, True))
      board, meta = encode(env.state, 'survival', 1)
      with torch.no_grad():
        before, old_value = original(*tensors([(board, meta, current[2])]))
        after, new_value = expanded(*tensors([current]))
      torch.testing.assert_close(before.logits, after.logits)
      torch.testing.assert_close(old_value, new_value)
    self.assertEqual(FEATURE_OBS_VERSION, expanded.observation_version)

  def test_mixed_training_really_updates_one_checkpoint_across_all_modes(self):
    torch.set_num_threads(1)
    with tempfile.TemporaryDirectory() as folder:
      path = Path(folder) / 'policy.pt'
      args = SimpleNamespace(seed=45, mixed_features=True, game_mode=None, color_profile='rare_seven',
        objective='survival', max_pieces=4, init_model=None, model=path, steps=48, rollout=48)
      train(args)
      trained, metadata = load_model(path)
      initial, _ = load_model(path.with_suffix('.initial.pt'))
      self.assertEqual(2, metadata['observation_version'])
      self.assertEqual(list(ALL_MODES), checkpoint_modes(metadata))
      self.assertEqual(48, metadata['steps'])
      self.assertFalse(torch.equal(initial.policy.weight, trained.policy.weight))
      import json
      report = json.loads(path.with_suffix('.json').read_text())
      self.assertEqual(set(mode.key for mode in ALL_MODES), {run['mode'] for run in report['episodes']})

  def test_one_policy_can_act_in_every_mode_and_checkpoint_reloads(self):
    torch.set_num_threads(1)
    model = ActorCritic(2)
    restored = ActorCritic(2)
    restored.load_state_dict(model.state_dict())
    self.assertEqual(12, len({mode.key for mode in ALL_MODES}))
    with PlacementEnv('survival', max_pieces=4, observation_version=2) as env:
      for mode in ALL_MODES:
        observation = env.reset(42, mode)
        self.assertEqual(mode.options(), {key: env.state[key] for key in mode.options()})
        for _ in range(4):
          with torch.no_grad():
            dist, value = restored(*tensors([observation]))
          self.assertTrue(torch.isfinite(value).all())
          self.assertEqual(0, dist.probs[0][~torch.from_numpy(observation[2])].sum().item())
          observation, _, done, info = env.step(dist.logits.argmax(-1).item())
        self.assertTrue(done)
        self.assertEqual(mode.key, info['mode'])
    metadata = {'color_profile': 'mixed', 'game_modes': [m.options() for m in ALL_MODES]}
    self.assertEqual(list(ALL_MODES), checkpoint_modes(metadata))
    self.assertEqual([ALL_MODES[-1]], checkpoint_modes(metadata, ALL_MODES[-1].key))
    with self.assertRaises(ValueError):
      checkpoint_modes({'color_profile': 'rare_seven'}, 'classic')
    with PlacementEnv('survival') as env:
      with self.assertRaises(ValueError):
        env.reset(42, GameMode(anchored_blocks=True))
