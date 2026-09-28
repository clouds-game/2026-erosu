"""真实规则的均衡教师样本、合法软标签，以及行为策略的数据血缘。"""
from pathlib import Path
from types import SimpleNamespace
import copy
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
try:
  import torch
except ImportError:
  torch = None

if torch is not None:
  from AI.engine import ENGINE
  from AI.mixed_imitation import collect, lineage, sha256, train, validate_dataset, validate_paths
  from AI.modes import ALL_MODES
  from AI.neural import ActorCritic
  from AI.ppo import load_model


@unittest.skipIf(torch is None, 'Install AI/requirements-rl.txt')
class MixedImitationTests(unittest.TestCase):
  def setUp(self):
    torch.set_num_threads(1)
    self.directory = tempfile.TemporaryDirectory()
    self.addCleanup(self.directory.cleanup)
    self.root = Path(self.directory.name)
    self.parent = {'observation_version': 2, 'objective': 'survival', 'max_pieces': 1,
      'color_profile': 'mixed', 'game_modes': [mode.options() for mode in ALL_MODES],
      'training_seeds': [6000000], 'validation_seeds': [6000001], 'engine_sha256': sha256(ENGINE)}

  def collect(self):
    return collect(self.root / 'data.pt', self.parent, 1, 6000000, 6000100)

  def test_real_collection_is_balanced_and_keeps_feature_and_seed_provenance(self):
    data = self.collect()
    self.assertEqual((12, 11, 18, 10), data['board'].shape)
    self.assertEqual(torch.float32, data['targets'].dtype)
    self.assertEqual([1] * 12, list(data['config']['mode_steps'].values()))
    self.assertEqual(list(range(6000002, 6000014)), data['config']['collection_seeds'])
    for index, mode in enumerate(ALL_MODES):
      self.assertEqual([mode.anchored_blocks, mode.enclosed_fill], data['metadata'][index, 73:75].tolist())
      self.assertEqual(index, int(data['sample_modes'][index]))
      self.assertFalse(data['config']['episodes'][index]['collection_truncated'])
    restored = torch.load(self.root / 'data.pt', weights_only=True)
    validate_dataset(restored, self.parent)

  def test_soft_ties_accept_legal_actions_and_reject_corrupt_data(self):
    data = self.collect()
    row = data['mask'][0].nonzero().flatten()
    data['targets'][0].zero_()
    data['targets'][0, row[:2]] = 0.5
    # Cached targets remain valid even if a producer returns double precision.
    data['targets'] = data['targets'].double()
    validate_dataset(data, self.parent)
    invalid = (~data['mask'][0]).nonzero().flatten()[0]
    broken = copy.deepcopy(data)
    broken['targets'][0, row[0]] = 0
    broken['targets'][0, invalid] = 0.5
    with self.assertRaisesRegex(ValueError, 'legal actions'):
      validate_dataset(broken, self.parent)
    broken = copy.deepcopy(data)
    broken['sample_modes'][0] = 1
    with self.assertRaisesRegex(ValueError, 'equal placement budgets'):
      validate_dataset(broken, self.parent)
    broken = copy.deepcopy(data)
    broken['sample_seeds'][0] = 42
    with self.assertRaisesRegex(ValueError, 'provenance'):
      validate_dataset(broken, self.parent)

  def test_behavior_collection_carries_both_lineages_and_rejects_stale_cache(self):
    init, behavior = self.root / 'init.pt', self.root / 'behavior.pt'
    torch.manual_seed(9)
    weights = ActorCritic(2).state_dict()
    torch.save({'metadata': self.parent, 'weights': weights}, init)
    behavior_metadata = {**self.parent, 'training_seeds': [6000002], 'validation_seeds': [6000003]}
    torch.save({'metadata': behavior_metadata, 'weights': weights}, behavior)
    args = SimpleNamespace(init_model=init, behavior_model=behavior, model=self.root / 'trained.pt',
      dataset=self.root / 'data.pt', steps_per_mode=1, seed_start=6000000, seed_stop=6000100,
      training_seed=9, epochs=1, batch_size=12)
    metadata = train(args)
    _, restored = load_model(args.model)
    self.assertEqual(metadata, restored)
    self.assertEqual([6000001, 6000003], restored['validation_seeds'])
    self.assertEqual([6000000, 6000002, *range(6000004, 6000016)], restored['training_seeds'])
    self.assertEqual(sha256(behavior), restored['behavior_model_sha256'])
    self.assertEqual('not_fitted_after_actor_imitation', restored['critic_status'])
    self.assertEqual('fixed_epochs', restored['selection'])
    data = torch.load(args.dataset, weights_only=True)
    self.assertEqual('greedy_model', data['config']['behavior'])
    self.assertEqual(sha256(behavior), data['config']['behavior_model_sha256'])
    args.model = self.root / 'second.pt'
    args.behavior_model = None
    with self.assertRaisesRegex(ValueError, 'behavior model mismatch'):
      train(args)
    self.assertFalse(args.model.exists())
    with self.assertRaisesRegex(ValueError, 'overlap'):
      lineage(self.parent, {**behavior_metadata, 'training_seeds': [6000001]})

  def test_output_collisions_are_rejected_before_any_writes(self):
    init, model, dataset = self.root / 'init.pt', self.root / 'model.pt', self.root / 'data.pt'
    with self.assertRaisesRegex(ValueError, 'must differ'):
      validate_paths(init, init, dataset)
    with self.assertRaisesRegex(ValueError, 'must differ'):
      validate_paths(init, model, dataset, behavior_model=model)
    model.with_suffix('.json').write_text('existing experiment')
    with self.assertRaisesRegex(ValueError, 'already exists'):
      validate_paths(init, model, dataset)
    self.assertEqual('existing experiment', model.with_suffix('.json').read_text())
    self.assertFalse(dataset.exists())


if __name__ == '__main__':
  unittest.main()
