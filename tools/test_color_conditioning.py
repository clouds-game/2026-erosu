"""颜色条件平面不改变观察格式；检查旧策略迁移、读取与继续 PPO。"""
from pathlib import Path
from types import SimpleNamespace
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
try:
  import torch
except ImportError:
  torch = None

if torch is not None:
  from AI.neural import ActorCritic, observation_size, upgrade, upgrade_color_conditioning
  from AI.ppo import load_model, train


@unittest.skipIf(torch is None, 'Install AI/requirements-rl.txt')
class ColorConditioningTests(unittest.TestCase):
  def setUp(self):
    torch.set_num_threads(1)
    torch.manual_seed(19)

  def observation(self, version):
    channels, meta_size = observation_size(version)
    board = torch.rand(2, channels, 18, 10)
    metadata = torch.rand(2, meta_size)
    metadata[:, 7:14] = 0
    metadata[0, 9] = metadata[1, 13] = 1
    mask = torch.ones(2, 40, dtype=torch.bool)
    mask[:, -2:] = False
    return board, metadata, mask

  def assertSameOutput(self, first, second):
    torch.testing.assert_close(first[0].logits, second[0].logits, atol=1e-6, rtol=1e-6)
    torch.testing.assert_close(first[1], second[1], atol=1e-6, rtol=1e-6)

  def test_zero_extension_preserves_both_old_observation_versions(self):
    for version in (1, 2):
      old = ActorCritic(version).eval()
      expanded = upgrade_color_conditioning(old)
      observation = self.observation(version)
      self.assertSameOutput(old(*observation), expanded(*observation))
      self.assertEqual(144, sum(p.numel() for p in expanded.parameters()) - sum(p.numel() for p in old.parameters()))
      self.assertFalse(expanded.training)
      self.assertTrue(expanded.color_conditioned)
      self.assertEqual(0, torch.count_nonzero(expanded.board[0].weight[:, -1]).item())
      self.assertIs(expanded, upgrade_color_conditioning(expanded))

  def test_match_plane_uses_active_color_and_can_learn_immediately(self):
    model = upgrade_color_conditioning(ActorCritic(2))
    observed = []
    handle = model.board[0].register_forward_pre_hook(lambda _, inputs: observed.append(inputs[0].detach().clone()))
    self.addCleanup(handle.remove)
    board, metadata, mask = self.observation(2)
    distribution, _ = model(board, metadata, mask)
    self.assertEqual((2, 12, 18, 10), observed[0].shape)
    torch.testing.assert_close(observed[0][:, :11], board)
    torch.testing.assert_close(observed[0][0, -1], board[0, 2])
    torch.testing.assert_close(observed[0][1, -1], board[1, 6])
    (-distribution.log_prob(torch.tensor([0, 1])).mean()).backward()
    self.assertGreater(model.board[0].weight.grad[:, -1].abs().sum().item(), 0)
    self.assertEqual(0, distribution.probs[~mask].sum().item())

  def test_observation_upgrade_relocates_learned_conditioning_weights(self):
    old = ActorCritic(1, color_conditioned=True).eval()
    with torch.no_grad():
      old.board[0].weight[:, -1].fill_(0.25)
    expanded = upgrade(old)
    board, metadata, mask = self.observation(1)
    new_board = torch.cat([board, torch.rand(2, 2, 18, 10)], dim=1)
    new_metadata = torch.cat([metadata, torch.rand(2, observation_size(2)[1] - metadata.shape[1])], dim=1)
    self.assertSameOutput(old(board, metadata, mask), expanded(new_board, new_metadata, mask))
    torch.testing.assert_close(expanded.board[0].weight[:, 11], old.board[0].weight[:, 9])
    self.assertEqual(0, torch.count_nonzero(expanded.board[0].weight[:, 9:11]).item())
    self.assertTrue(expanded.color_conditioned)
    self.assertEqual(2, expanded.observation_version)

  def test_old_loading_and_conditioned_ppo_continuation_preserve_format(self):
    with tempfile.TemporaryDirectory() as directory:
      root = Path(directory)
      parent = {'observation_version': 1, 'objective': 'survival', 'max_pieces': 2,
        'color_profile': 'rare_seven', 'training_seeds': [7], 'validation_seeds': [8]}
      old = ActorCritic(1)
      torch.save({'metadata': parent, 'weights': old.state_dict()}, root / 'old.pt')
      loaded, _ = load_model(root / 'old.pt')
      self.assertFalse(loaded.color_conditioned)
      conditioned = upgrade_color_conditioning(old)
      torch.save({'metadata': {**parent, 'color_conditioned': True},
        'weights': conditioned.state_dict()}, root / 'conditioned.pt')
      loaded, _ = load_model(root / 'conditioned.pt')
      observation = self.observation(1)
      self.assertSameOutput(conditioned(*observation), loaded(*observation))
      args = SimpleNamespace(init_model=root / 'conditioned.pt', model=root / 'continued.pt',
        objective='survival', max_pieces=2, color_profile='rare_seven', seed=99, steps=2, rollout=2)
      train(args)
      continued, metadata = load_model(args.model)
      self.assertTrue(metadata['color_conditioned'])
      self.assertTrue(continued.color_conditioned)
      self.assertEqual(10, continued.board[0].in_channels)


if __name__ == '__main__':
  unittest.main()
