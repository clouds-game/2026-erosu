"""可选 PyTorch 测试；CI 的 neural job 必须安装依赖后运行。"""
from pathlib import Path
import copy
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
try:
  import torch
  import numpy as np
except ImportError:
  torch = None

if torch is not None:
  from AI.neural import ActorCritic, encode
  from AI.ppo import advantages, tensors, update
  from AI.rl_env import PlacementEnv


@unittest.skipIf(torch is None, 'Install AI/requirements-rl.txt for neural tests')
class NeuralTests(unittest.TestCase):
  def test_encoding_preserves_identity_without_numeric_ids(self):
    env = PlacementEnv('survival')
    self.addCleanup(env.close)
    env.reset(42)
    state = copy.deepcopy(env.state)
    state['pieces'] = [
      {'id': 1, 'color': 0, 'cells': [{'x': 0, 'y': 17}, {'x': 1, 'y': 17}]},
      {'id': 2, 'color': 0, 'cells': [{'x': 2, 'y': 17}, {'x': 3, 'y': 17}]}]
    board, metadata = encode(state, 'survival', 1)
    self.assertEqual((9, 18, 10), board.shape)
    self.assertEqual(1, board[7, 17, 0])
    self.assertEqual(0, board[7, 17, 1])
    state['pieces'][0]['id'] = 999
    np.testing.assert_array_equal(board, encode(state, 'survival', 1)[0])
    state['pieces'][0]['cells'] += state['pieces'].pop()['cells']
    self.assertEqual(1, encode(state, 'survival', 1)[0][7, 17, 1])
    self.assertEqual(73, len(metadata))
    self.assertAlmostEqual(0.03, float(metadata[62]), places=6)
    self.assertEqual(1, metadata[69])

  def test_gae_does_not_cross_episode_boundaries(self):
    adv, returns = advantages([1, 2, 3], [0, 0, 0], [False, True, False], 4, gamma=1, lam=1)
    torch.testing.assert_close(returns, torch.tensor([3., 2., 7.]))
    torch.testing.assert_close(adv, returns)

  def test_mask_rewards_and_real_ppo_update(self):
    torch.set_num_threads(1)
    torch.manual_seed(8)
    model = ActorCritic()
    env = PlacementEnv('survival', max_pieces=4)
    self.addCleanup(env.close)
    observation = env.reset(42)
    observations, actions, logs, values, rewards, dones = [], [], [], [], [], []
    for _ in range(4):
      with torch.no_grad():
        dist, value = model(*tensors([observation]))
        self.assertEqual(0, dist.probs[0][~torch.from_numpy(observation[2])].sum().item())
        action = dist.sample()
      observations.append(observation)
      actions.append(action.item())
      logs.append(dist.log_prob(action).item())
      values.append(value.item())
      observation, reward, done, info = env.step(action.item())
      self.assertEqual(1, reward)
      rewards.append(reward)
      dones.append(done)
    self.assertTrue(done and info['capped'] and not info['terminated'])
    adv, returns = advantages(rewards, values, dones, 0)
    before = model.policy.weight.detach().clone()
    loss = update(model, torch.optim.Adam(model.parameters(), lr=1e-3),
      (*tensors(observations), torch.tensor(actions), torch.tensor(logs), returns, adv), epochs=1)
    self.assertTrue(np.isfinite(loss))
    self.assertFalse(torch.equal(before, model.policy.weight))

  def test_score_reward_uses_resolved_game_score(self):
    env = PlacementEnv('score')
    self.addCleanup(env.close)
    env.state = env.engine.command('start', mode='puzzle', level=1)['state']
    env.seed = 0
    observation, reward, done, info = env.step(4)
    self.assertEqual(2.1, reward)
    self.assertTrue(done and info['terminated'])
    self.assertIsNone(observation)
    self.assertEqual(2100, info['score'])
