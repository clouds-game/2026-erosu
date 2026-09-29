"""十二种模式的真实引擎训练、检查点兼容性和实验输出保护。"""
from contextlib import redirect_stdout
import hashlib
import io
import json
from pathlib import Path
import random
import sys
import tempfile
from types import SimpleNamespace
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
try:
  import torch
except ImportError:
  torch = None

if torch is not None:
  from AI.engine import ENGINE
  from AI.modes import ALL_MODES
  from AI.neural import ActorCritic, FEATURE_OBS_VERSION, upgrade_color_conditioning
  from AI.ppo import checkpoint_modes, load_model, play
  from AI.ppo_balanced import train


@unittest.skipIf(torch is None, 'Install AI/requirements-rl.txt for neural tests')
class BalancedTrainingTests(unittest.TestCase):
  def setUp(self):
    torch.set_num_threads(1)
    torch.manual_seed(31)
    self.folder = tempfile.TemporaryDirectory()
    self.addCleanup(self.folder.cleanup)
    self.root = Path(self.folder.name)

  def parent(self, max_pieces=2, name='parent.pt'):
    model = ActorCritic(FEATURE_OBS_VERSION)
    # Force the first two candidate training seeds to be excluded by lineage or validation.
    rng = random.Random(49)
    old_training_seed, validation_seed = rng.randrange(7_000_000, 8_000_000), rng.randrange(7_000_000, 8_000_000)
    metadata = {'observation_version': FEATURE_OBS_VERSION, 'objective': 'survival',
      'color_profile': 'mixed', 'game_modes': [mode.options() for mode in ALL_MODES],
      'max_pieces': max_pieces, 'training_seed': 31, 'training_seeds': [123, old_training_seed],
      'validation_seeds': [validation_seed], 'steps': 0,
      'engine_sha256': hashlib.sha256(ENGINE.read_bytes()).hexdigest()}
    path = self.root / name
    torch.save({'metadata': metadata, 'weights': model.state_dict()}, path)
    return path, model, metadata

  def args(self, parent, **changes):
    values = dict(init_model=parent, model=self.root / 'trained.pt', steps=60,
      rollout=2, minibatch=24, epochs=1, learning_rate=3e-4, target_kl=0.02, seed=49)
    return SimpleNamespace(**(values | changes))

  def test_all_modes_receive_equal_steps_and_survive_checkpoint_and_replay(self):
    for max_pieces, steps in ((2, 60), (4, 48)):
      with self.subTest(max_pieces=max_pieces):
        parent_path, initial, parent_metadata = self.parent(max_pieces, f'parent-{max_pieces}.pt')
        args = self.args(parent_path, model=self.root / f'trained-{max_pieces}.pt', steps=steps,
          teacher_coef=0.5 if max_pieces == 4 else 0.0)
        parent_bytes = parent_path.read_bytes()
        with redirect_stdout(io.StringIO()):
          train(args)
        model, metadata = load_model(args.model)
        self.assertEqual(parent_bytes, parent_path.read_bytes())
        self.assertFalse(torch.equal(initial.policy.weight, model.policy.weight))
        self.assertEqual(list(ALL_MODES), checkpoint_modes(metadata))
        self.assertEqual(steps, metadata['steps'])
        self.assertEqual(steps if args.teacher_coef else 0, metadata['on_policy_teacher_labels'])
        expected_modes = {mode.key for mode in ALL_MODES}
        self.assertEqual(expected_modes, set(metadata['mode_steps']))
        self.assertEqual({steps // len(ALL_MODES)}, set(metadata['mode_steps'].values()))
        self.assertTrue(set(parent_metadata['training_seeds']) <= set(metadata['training_seeds']))
        self.assertFalse(set(metadata['training_seeds']) & set(parent_metadata['validation_seeds']))
        self.assertEqual(parent_metadata['validation_seeds'], metadata['validation_seeds'])
        report = json.loads(args.model.with_suffix('.json').read_text())
        self.assertEqual(expected_modes, {episode['mode'] for episode in report['episodes']})
        episode_seeds = {episode['seed'] for episode in report['episodes']}
        self.assertFalse(episode_seeds & set(parent_metadata['training_seeds']))
        self.assertFalse(episode_seeds & set(parent_metadata['validation_seeds']))
        self.assertTrue(all(episode['locked'] == max_pieces and episode['capped']
          and not episode['terminated'] for episode in report['episodes']))
        expected_episodes = steps // len(ALL_MODES) // max_pieces
        self.assertEqual({expected_episodes}, set(report['history'][-1]['mode_episodes'].values()))
        for row in report['history']:
          self.assertEqual({row['steps'] // len(ALL_MODES)}, set(row['mode_steps'].values()))
        rates = [row['learning_rate'] for row in report['history']]
        self.assertEqual(sorted(rates, reverse=True), rates)
        self.assertAlmostEqual(args.learning_rate, rates[0])
        self.assertAlmostEqual(args.learning_rate * 0.1, rates[-1])
        self.assertFalse(args.model.with_suffix('.tmp').exists())
        trace = self.root / f'replay-{max_pieces}.jsonl'
        output = io.StringIO()
        with redirect_stdout(output):
          play(SimpleNamespace(model=args.model, trace=trace,
            game_mode=ALL_MODES[-1].key, seed=12_000_001))
        replay = json.loads(output.getvalue())
        self.assertTrue(replay['verified'])
        self.assertEqual(max_pieces, replay['result']['locked'])
        self.assertEqual(ALL_MODES[-1].key, replay['result']['mode'])

  def test_rejects_invalid_budgets_before_creating_outputs(self):
    parent_path, _, _ = self.parent()
    cases = [dict(steps=0), dict(steps=13), dict(rollout=0), dict(minibatch=0),
      dict(epochs=0), dict(learning_rate=0), dict(learning_rate=float('nan')),
      dict(learning_rate=float('inf')), dict(target_kl=0), dict(target_kl=float('inf')),
      dict(teacher_coef=-1), dict(teacher_coef=float('nan'))]
    for changes in cases:
      with self.subTest(**changes):
        args = self.args(parent_path, **changes)
        with self.assertRaises(ValueError):
          train(args)
        self.assertFalse(args.model.exists())
        self.assertFalse(args.model.with_suffix('.json').exists())

  def test_conditioned_policy_remains_loadable_after_guided_balanced_training(self):
    parent_path, initial, parent_metadata = self.parent()
    conditioned = upgrade_color_conditioning(initial)
    parent_metadata.update(color_conditioned=True,
      parameters=sum(parameter.numel() for parameter in conditioned.parameters()))
    torch.save({'metadata': parent_metadata, 'weights': conditioned.state_dict()}, parent_path)
    args = self.args(parent_path, steps=24, teacher_coef=0.5)
    with redirect_stdout(io.StringIO()):
      train(args)
    model, metadata = load_model(args.model)
    self.assertTrue(metadata['color_conditioned'])
    self.assertTrue(model.color_conditioned)
    self.assertEqual(12, model.board[0].in_channels)
    self.assertEqual(metadata['parameters'], sum(parameter.numel() for parameter in model.parameters()))
    self.assertEqual({2}, set(metadata['mode_steps'].values()))
    self.assertEqual(24, metadata['on_policy_teacher_labels'])
    self.assertFalse(torch.equal(conditioned.policy.weight, model.policy.weight))
    output = io.StringIO()
    with redirect_stdout(output):
      play(SimpleNamespace(model=args.model, trace=self.root / 'conditioned-replay.jsonl',
        game_mode=ALL_MODES[-1].key, seed=12_000_002))
    replay = json.loads(output.getvalue())
    self.assertTrue(replay['verified'])
    self.assertEqual(2, replay['result']['locked'])

  def test_rejects_parent_and_output_path_collisions_without_overwriting(self):
    for suffix in ('.pt', '.json', '.tmp'):
      with self.subTest(parent_suffix=suffix):
        parent_path, _, _ = self.parent(name=f'protected-{suffix[1:]}{suffix}')
        original = parent_path.read_bytes()
        with self.assertRaises(ValueError):
          train(self.args(parent_path, model=parent_path.with_suffix('.pt')))
        self.assertEqual(original, parent_path.read_bytes())
    parent_path, _, _ = self.parent()
    for suffix in ('.json', '.tmp'):
      with self.subTest(output_suffix=suffix):
        path = self.root / f'aliased{suffix}'
        with self.assertRaises(ValueError):
          train(self.args(parent_path, model=path))
        self.assertFalse(path.exists())
    existing = self.root / 'existing.pt'
    existing.write_bytes(b'preserve existing experiment')
    with self.assertRaises(ValueError):
      train(self.args(parent_path, model=existing))
    self.assertEqual(b'preserve existing experiment', existing.read_bytes())

  def test_rejects_checkpoint_from_another_engine(self):
    parent_path, model, metadata = self.parent()
    metadata['engine_sha256'] = 'different engine'
    torch.save({'metadata': metadata, 'weights': model.state_dict()}, parent_path)
    with self.assertRaisesRegex(ValueError, 'engine differs'):
      train(self.args(parent_path))


if __name__ == '__main__':
  unittest.main()
