"""十二种规则按落块数均衡采样教师轨迹，用软标签预热同一个 v2 策略。"""
import argparse
import hashlib
import json
from pathlib import Path
import time

import numpy as np
import torch

from .engine import ENGINE
from .imitation import teacher_targets
from .modes import ALL_MODES
from .neural import FEATURE_OBS_VERSION, observation_size, upgrade_color_conditioning
from .ppo import load_model, tensors
from .rl_env import PlacementEnv
from .train import save, source_hash

DATASET_VERSION = 1
DATA_KEYS = ('board', 'metadata', 'mask', 'targets', 'sample_seeds', 'sample_modes')


def sha256(path):
  return hashlib.sha256(path.read_bytes()).hexdigest()


def validate_paths(init_model, model, dataset, behavior_model=None):
  outputs = [model, model.with_suffix('.json'), model.with_suffix('.tmp'), dataset, dataset.with_suffix('.tmp')]
  resolved = {path.resolve() for path in outputs}
  inputs = [init_model] + ([behavior_model] if behavior_model is not None else [])
  if len(resolved) != len(outputs) or any(path.resolve() in resolved for path in inputs):
    raise ValueError('Initialization, checkpoint, dataset and report paths must differ')
  if any(path.exists() for path in outputs[:3]):
    raise ValueError('Checkpoint or report output already exists')


def lineage(parent, behavior=None):
  training = set(parent['training_seeds']) | set((behavior or {}).get('training_seeds', []))
  validation = set(parent.get('validation_seeds', [])) | set((behavior or {}).get('validation_seeds', []))
  if training & validation:
    raise ValueError('Initialization and behavior training/validation seeds overlap')
  return {**parent, 'training_seeds': sorted(training), 'validation_seeds': sorted(validation)}


def collect(path, parent, steps_per_mode, seed_start, seed_stop, behavior_model=None, behavior_hash=None):
  excluded = set(parent['training_seeds']) | set(parent.get('validation_seeds', []))
  available = seed_stop - seed_start - sum(seed_start <= seed < seed_stop for seed in excluded)
  if steps_per_mode < 1 or not 0 <= seed_start < seed_stop <= 2**31 or available < steps_per_mode * len(ALL_MODES):
    raise ValueError('Require a positive budget and enough unused nonnegative Int32 episode seeds')
  config = {'version': DATASET_VERSION, 'observation_version': FEATURE_OBS_VERSION,
    'objective': parent['objective'], 'max_pieces': parent['max_pieces'],
    'game_modes': [mode.options() for mode in ALL_MODES], 'steps_per_mode': steps_per_mode,
    'seed_start': seed_start, 'seed_stop': seed_stop, 'teacher': 'legal_placement_baseline',
    'source_sha256': source_hash(), 'engine_sha256': sha256(ENGINE),
    'behavior_model_sha256': behavior_hash, 'behavior': 'greedy_model' if behavior_model is not None else 'teacher',
    'collection_seeds': [], 'episodes': [], 'mode_steps': {}}
  boards, metadata, masks, targets, sample_seeds, sample_modes = [], [], [], [], [], []
  seeds = (seed for seed in range(seed_start, seed_stop) if seed not in excluded)
  started = time.monotonic()
  with PlacementEnv(config['objective'], config['max_pieces'], observation_version=FEATURE_OBS_VERSION) as env:
    for mode_index, mode in enumerate(ALL_MODES):
      count = 0
      while count < steps_per_mode:
        seed = next(seeds)
        config['collection_seeds'].append(seed)
        observation = env.reset(seed, mode)
        while count < steps_per_mode:
          target = teacher_targets(env.state, env.actions)
          boards.append(observation[0])
          metadata.append(observation[1])
          masks.append(observation[2])
          targets.append(target)
          sample_seeds.append(seed)
          sample_modes.append(mode_index)
          action = int(target.argmax())
          if behavior_model is not None:
            with torch.no_grad():
              distribution, _ = behavior_model(*tensors([observation]))
              action = int(distribution.logits.argmax(-1).item())
          observation, _, done, info = env.step(action)
          count += 1
          if done or count == steps_per_mode:
            config['episodes'].append({**info, 'collection_truncated': not done})
            break
      config['mode_steps'][mode.key] = count
      print(json.dumps({'collected_mode': mode.key, 'steps': count,
        'elapsed_seconds': time.monotonic() - started}), flush=True)
  config['collection_seconds'] = time.monotonic() - started
  data = {'config': config, 'board': torch.from_numpy(np.stack(boards).astype(np.uint8)),
    'metadata': torch.from_numpy(np.stack(metadata)), 'mask': torch.from_numpy(np.stack(masks)),
    'targets': torch.from_numpy(np.stack(targets).astype(np.float32)), 'sample_seeds': torch.tensor(sample_seeds),
    'sample_modes': torch.tensor(sample_modes)}
  validate_dataset(data, parent)
  path.parent.mkdir(parents=True, exist_ok=True)
  temporary = path.with_suffix('.tmp')
  if path.exists() or temporary.exists():
    raise ValueError('Demonstration output already exists')
  torch.save(data, temporary)
  temporary.replace(path)
  return data


def validate_dataset(data, parent):
  config = data['config']
  if config['version'] != DATASET_VERSION or config['observation_version'] != FEATURE_OBS_VERSION:
    raise ValueError('Incompatible demonstration observation version')
  if config['game_modes'] != [mode.options() for mode in ALL_MODES]:
    raise ValueError('Demonstrations must cover all twelve game modes')
  for key in ('objective', 'max_pieces'):
    if config[key] != parent[key]:
      raise ValueError(f'Demonstration task mismatch: {key}')
  if config['engine_sha256'] != sha256(ENGINE):
    raise ValueError('Demonstration engine mismatch')
  count = config['steps_per_mode'] * len(ALL_MODES)
  channels, meta_size = observation_size(FEATURE_OBS_VERSION)
  shapes = ((count, channels, 18, 10), (count, meta_size), (count, 40), (count, 40), (count,), (count,))
  if any(data[key].shape != shape for key, shape in zip(DATA_KEYS, shapes)):
    raise ValueError('Malformed demonstration tensors')
  mask, targets = data['mask'], data['targets']
  if (mask.dtype != torch.bool or not mask.any(-1).all() or not torch.isfinite(targets).all()
      or (targets < 0).any() or targets[~mask].any()
      or not torch.allclose(targets.sum(-1), torch.ones_like(targets.sum(-1)))):
    raise ValueError('Teacher targets must be normalized distributions over legal actions')
  indices = data['sample_modes']
  if (indices < 0).any() or (indices >= len(ALL_MODES)).any():
    raise ValueError('Unknown demonstration mode')
  for index, mode in enumerate(ALL_MODES):
    if int((indices == index).sum()) != config['steps_per_mode']:
      raise ValueError('Demonstrations must have equal placement budgets per mode')
    if config['mode_steps'].get(mode.key) != config['steps_per_mode']:
      raise ValueError('Demonstration count metadata mismatch')
  seeds = set(data['sample_seeds'].tolist())
  if seeds != set(config['collection_seeds']) or len(config['collection_seeds']) != len(seeds):
    raise ValueError('Demonstration seed provenance mismatch')
  if any(not config['seed_start'] <= seed < config['seed_stop'] for seed in seeds):
    raise ValueError('Demonstration seed outside collection range')
  if seeds & (set(parent['training_seeds']) | set(parent.get('validation_seeds', []))):
    raise ValueError('Demonstration seeds overlap initialization training or validation')


def batches(data, batch_size, shuffle=False):
  count = len(data['board'])
  for indices in (torch.randperm(count) if shuffle else torch.arange(count)).split(batch_size):
    yield tuple(data[key][indices] for key in DATA_KEYS[:4])


def metrics(model, data, batch_size):
  loss = correct = count = 0
  with torch.no_grad():
    for board, meta, mask, target in batches(data, batch_size):
      distribution, _ = model(board.float(), meta, mask)
      loss += (-(target * distribution.logits).sum(-1)).sum().item()
      predicted = distribution.logits.argmax(-1)
      correct += (target.gather(1, predicted[:, None]) > 0).sum().item()
      count += len(board)
  return {'cross_entropy': loss / count, 'teacher_agreement': correct / count}


def train(args):
  behavior_path = getattr(args, 'behavior_model', None)
  validate_paths(args.init_model, args.model, args.dataset, behavior_path)
  if min(args.epochs, args.batch_size, args.steps_per_mode) < 1:
    raise ValueError('Require positive training budgets')
  model, parent = load_model(args.init_model)
  if parent['observation_version'] != FEATURE_OBS_VERSION or parent['objective'] != 'survival':
    raise ValueError('Initialization requires a v2 survival checkpoint')
  if parent['engine_sha256'] != sha256(ENGINE):
    raise ValueError('Initialization engine mismatch')
  if getattr(args, 'color_conditioned', False):
    model = upgrade_color_conditioning(model)
  behavior_model, behavior_metadata = None, None
  if behavior_path is not None:
    behavior_model, behavior_metadata = load_model(behavior_path)
    for key in ('observation_version', 'objective', 'max_pieces', 'engine_sha256'):
      if behavior_metadata[key] != parent[key]:
        raise ValueError(f'Behavior model task mismatch: {key}')
  provenance = lineage(parent, behavior_metadata)
  behavior_hash = sha256(behavior_path) if behavior_path is not None else None
  torch.manual_seed(args.training_seed)
  if args.dataset.exists():
    data = torch.load(args.dataset, map_location='cpu', weights_only=True)
    validate_dataset(data, provenance)
    if data['config'].get('behavior_model_sha256') != behavior_hash:
      raise ValueError('Cached demonstration behavior model mismatch')
    for key in ('steps_per_mode', 'seed_start', 'seed_stop'):
      if data['config'][key] != getattr(args, key):
        raise ValueError(f'Cached demonstration settings mismatch: {key}')
  else:
    data = collect(args.dataset, provenance, args.steps_per_mode, args.seed_start, args.seed_stop,
      behavior_model, behavior_hash)
  optimizer = torch.optim.Adam(model.parameters(), lr=3e-4)
  initial = metrics(model, data, args.batch_size)
  history = []
  started = time.monotonic()
  for epoch in range(1, args.epochs + 1):
    model.train()
    for board, meta, mask, target in batches(data, args.batch_size, shuffle=True):
      distribution, _ = model(board.float(), meta, mask)
      loss = -(target * distribution.logits).sum(-1).mean()
      if not torch.isfinite(loss):
        raise RuntimeError('Non-finite imitation loss')
      optimizer.zero_grad()
      loss.backward()
      torch.nn.utils.clip_grad_norm_(model.parameters(), 0.5)
      optimizer.step()
    model.eval()
    history.append({'epoch': epoch, 'training': metrics(model, data, args.batch_size),
      'elapsed_seconds': time.monotonic() - started})
    print(json.dumps(history[-1]), flush=True)
  metadata = {**parent, 'algorithm': 'mixed_behavior_cloning', 'steps': 0,
    'color_conditioned': model.color_conditioned, 'parameters': sum(p.numel() for p in model.parameters()),
    'color_profile': 'mixed', 'game_modes': [mode.options() for mode in ALL_MODES],
    'training_seed': args.training_seed,
    'training_seeds': sorted(set(provenance['training_seeds']) | set(data['config']['collection_seeds'])),
    'validation_seeds': provenance['validation_seeds'], 'behavior_model_sha256': behavior_hash,
    'initialization_sha256': sha256(args.init_model), 'dataset_sha256': sha256(args.dataset),
    'source_sha256': source_hash(), 'engine_sha256': data['config']['engine_sha256'],
    'demonstration_steps': len(data['board']), 'epochs': args.epochs, 'batch_size': args.batch_size,
    'selection': 'fixed_epochs', 'critic_status': 'not_fitted_after_actor_imitation',
    'torch_version': str(torch.__version__)}
  args.model.parent.mkdir(parents=True, exist_ok=True)
  temporary = args.model.with_suffix('.tmp')
  torch.save({'metadata': metadata, 'weights': model.state_dict()}, temporary)
  temporary.replace(args.model)
  save(args.model.with_suffix('.json'), {'metadata': metadata, 'dataset': str(args.dataset),
    'initial_training': initial, 'history': history, 'collection': data['config'],
    'training_seconds': time.monotonic() - started})
  return metadata


def main():
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument('--init-model', type=Path, required=True)
  parser.add_argument('--model', type=Path, required=True)
  parser.add_argument('--dataset', type=Path, required=True)
  parser.add_argument('--behavior-model', type=Path,
    help='Collect teacher labels on this model\'s greedy trajectories instead of teacher trajectories')
  parser.add_argument('--color-conditioned', action='store_true',
    help='Expose cells matching the active piece color to the first convolution')
  parser.add_argument('--steps-per-mode', type=int, default=1024)
  parser.add_argument('--seed-start', type=int, default=6000000)
  parser.add_argument('--seed-stop', type=int, default=7000000)
  parser.add_argument('--training-seed', type=int, default=42)
  parser.add_argument('--epochs', type=int, default=10)
  parser.add_argument('--batch-size', type=int, default=128)
  parser.add_argument('--threads', type=int, default=1)
  args = parser.parse_args()
  if args.threads < 1:
    parser.error('threads must be positive')
  torch.set_num_threads(args.threads)
  train(args)


if __name__ == '__main__':
  main()
