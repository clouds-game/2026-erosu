"""多环境轨迹边界、PPO 更新和策略偏移限制的回归测试。"""
from pathlib import Path
import math
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
try:
  import torch
except ImportError:
  torch = None

if torch is not None:
  from AI.neural import ActorCritic, FEATURE_OBS_VERSION, FEATURE_META_SIZE
  from AI.ppo_optimization import advantages, update


@unittest.skipIf(torch is None, 'Install AI/requirements-rl.txt for neural tests')
class OptimizationTests(unittest.TestCase):
  def setUp(self):
    torch.set_num_threads(1)
    torch.manual_seed(19)

  def test_gae_keeps_environments_and_reset_boundaries_separate(self):
    rewards = torch.tensor([[1., 10.], [2., 20.], [3., 30.]])
    values = torch.tensor([[0.5, 5.], [1., 6.], [1.5, 7.]])
    dones = torch.tensor([[False, True], [True, False], [False, False]])
    actual, returns = advantages(rewards, values, dones, torch.tensor([4., 40.]), gamma=1, lam=1)
    expected = torch.tensor([[3., 10.], [2., 90.], [7., 70.]])
    torch.testing.assert_close(returns, expected)
    torch.testing.assert_close(actual, expected - values)

  def test_gae_terminal_step_ignores_its_bootstrap_only(self):
    values = torch.tensor([[1., 3.]], dtype=torch.float64, requires_grad=True)
    actual, returns = advantages([[2., 5.]], values, [[False, True]], [4., 100.], gamma=0.5)
    torch.testing.assert_close(returns, torch.tensor([[4., 5.]], dtype=torch.float64))
    torch.testing.assert_close(actual, torch.tensor([[3., 2.]], dtype=torch.float64))
    self.assertFalse(actual.requires_grad or returns.requires_grad)
    with self.assertRaisesRegex(ValueError, 'one value per environment'):
      advantages([[2., 5.]], values, [[False, True]], [4.])

  def batch(self):
    model = ActorCritic(FEATURE_OBS_VERSION)
    count = 16
    board = torch.rand(count, 11, 18, 10)
    meta = torch.rand(count, FEATURE_META_SIZE)
    mask = torch.zeros(count, 40, dtype=torch.bool)
    mask[:, :12] = True
    mask[::2, 3] = False
    with torch.no_grad():
      distribution, values = model(board, meta, mask)
      actions = distribution.sample()
      old_logs = distribution.log_prob(actions)
    returns = torch.arange(count, dtype=torch.float32) / 2
    return model, (board, meta, mask, actions, old_logs, returns, returns - values)

  def test_update_changes_weights_with_finite_diagnostics_and_keeps_mask(self):
    model, batch = self.batch()
    before = model.policy.weight.detach().clone()
    metrics = update(model, torch.optim.Adam(model.parameters(), lr=1e-3), batch,
      epochs=2, minibatch=8, target_kl=None)
    self.assertFalse(torch.equal(before, model.policy.weight))
    self.assertTrue(all(math.isfinite(value) for value in metrics.values()))
    self.assertEqual(4, metrics['optimizer_steps'])
    self.assertEqual(2, metrics['epochs'])
    self.assertFalse(metrics['early_stopped'])
    self.assertGreater(metrics['grad_norm'], 0)
    distribution, _ = model(*batch[:3])
    self.assertEqual(0, distribution.probs[~batch[2]].sum().item())

  def test_kl_limit_stops_before_applying_an_outdated_rollout(self):
    model, batch = self.batch()
    stale_batch = (*batch[:4], batch[4] + 1, *batch[5:])
    before = {key: value.clone() for key, value in model.state_dict().items()}
    metrics = update(model, torch.optim.Adam(model.parameters(), lr=1e-3), stale_batch)
    self.assertTrue(metrics['early_stopped'])
    self.assertEqual(0, metrics['optimizer_steps'])
    self.assertGreater(metrics['kl'], 0.02)
    for key, value in model.state_dict().items():
      torch.testing.assert_close(value, before[key], rtol=0, atol=0)

  def test_kl_limit_stops_following_epochs_after_a_large_update(self):
    model, batch = self.batch()
    metrics = update(model, torch.optim.Adam(model.parameters(), lr=0.05), batch,
      epochs=4, minibatch=len(batch[3]), target_kl=1e-6)
    self.assertTrue(metrics['early_stopped'])
    self.assertEqual(1, metrics['optimizer_steps'])
    self.assertEqual(2, metrics['epochs'])

  def test_rejects_masked_rollout_actions(self):
    model, batch = self.batch()
    actions = batch[3].clone()
    actions[0] = 39
    with self.assertRaisesRegex(ValueError, 'masked action'):
      update(model, torch.optim.Adam(model.parameters()), (*batch[:3], actions, *batch[4:]))

  def test_teacher_guidance_updates_the_actor_on_rollout_observations(self):
    model, batch = self.batch()
    targets = torch.zeros_like(batch[2], dtype=torch.float32)
    targets[:, 0] = 0.5
    targets[:, 1] = 0.5
    with torch.no_grad():
      distribution, _ = model(*batch[:3])
      before = -(targets * distribution.logits).sum(-1).mean().item()
    no_advantage = (*batch[:6], torch.zeros_like(batch[6]))
    metrics = update(model, torch.optim.Adam(model.parameters(), lr=1e-4), no_advantage,
      epochs=1, minibatch=len(batch[3]), value_coef=0, entropy_coef=0,
      teacher_targets=targets, teacher_coef=0.5)
    with torch.no_grad():
      distribution, _ = model(*batch[:3])
      after = -(targets * distribution.logits).sum(-1).mean().item()
    self.assertLess(after, before)
    self.assertGreater(metrics['teacher_loss'], 0)
    self.assertTrue(all(math.isfinite(value) for value in metrics.values()))
    self.assertEqual(0, distribution.probs[~batch[2]].sum().item())

  def test_rejects_invalid_teacher_targets(self):
    model, batch = self.batch()
    targets = torch.zeros_like(batch[2], dtype=torch.float32)
    targets[:, 0] = 1
    masked = targets.clone()
    masked[0, 0], masked[0, 39] = 0, 1
    negative = targets.clone()
    negative[0, 0], negative[0, 1] = 2, -1
    nonfinite = targets.clone()
    nonfinite[0, 0] = float('nan')
    for invalid in (masked, negative, nonfinite, targets[:, :2], targets * 0.5):
      with self.subTest(shape=tuple(invalid.shape)):
        with self.assertRaises(ValueError):
          update(model, torch.optim.Adam(model.parameters()), batch,
            teacher_targets=invalid, teacher_coef=0.5)
    with self.assertRaisesRegex(ValueError, 'requires targets'):
      update(model, torch.optim.Adam(model.parameters()), batch, teacher_coef=0.5)


if __name__ == '__main__':
  unittest.main()
