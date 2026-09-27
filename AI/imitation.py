"""合法落点教师示范、按 episode 分离的行为克隆与检查点导出。"""

import argparse
import hashlib
import json
from pathlib import Path
import statistics

import numpy as np
import torch

from .engine import ENGINE
from .neural import ActorCritic, OBS_VERSION
from .policy import BASELINE, placement_features
from .rl_env import PlacementEnv
from .train import save, source_hash


def teacher_targets(state, actions):
  scores = np.full(40, -np.inf, dtype=np.float32)
  occupied = {(c['x'], c['y']): p for p in state['pieces'] for c in p['cells']}
  for option in actions:
    if option['piece'] is not None:
      landing = {(c['x'], c['y']) for c in option['piece']['cells']}
      features = placement_features(state, landing, occupied)
      scores[option['action']] = sum(w * f for w, f in zip(BASELINE, features))
  if not np.isfinite(scores).any():
    raise ValueError('Teacher requires a legal action')
  # Equivalent orientations and exact ties should not give contradictory labels.
  best = np.isclose(scores, scores.max(), rtol=0, atol=1e-6)
  return best.astype(np.float32) / best.sum()


def collect(path, seed, steps, max_pieces=300, profile='rare_seven'):
  boards, metadata, masks, targets, seeds, episodes = [], [], [], [], [], []
  with PlacementEnv('survival', max_pieces, profile) as env:
    while len(boards) < steps:
      current_seed = seed + len(seeds)
      seeds.append(current_seed)
      observation = env.reset(current_seed)
      while True:
        target = teacher_targets(env.state, env.actions)
        boards.append(observation[0])
        metadata.append(observation[1])
        masks.append(observation[2])
        targets.append(target)
        observation, _, done, info = env.step(target.argmax())
        if done:
          episodes.append(info)
          break
  config = {'observation_version': OBS_VERSION, 'objective': 'survival', 'max_pieces': max_pieces,
    'color_profile': profile, 'seeds': seeds, 'steps': len(boards), 'episodes': episodes,
    'teacher': 'legal_placement_baseline', 'source_sha256': source_hash(),
    'engine_sha256': hashlib.sha256(ENGINE.read_bytes()).hexdigest()}
  path.parent.mkdir(parents=True, exist_ok=True)
  np.savez_compressed(path, board=np.stack(boards).astype(np.uint8), metadata=np.stack(metadata),
    mask=np.stack(masks), targets=np.stack(targets), config=json.dumps(config))
  save(path.with_suffix('.json'), config)
  print(json.dumps({'dataset': str(path), 'steps': len(boards), 'episodes': len(episodes),
    'mean_locked': statistics.mean(e['locked'] for e in episodes)}), flush=True)


def read_dataset(path):
  with np.load(path, allow_pickle=False) as data:
    config = json.loads(str(data['config']))
    tensors = tuple(torch.from_numpy(data[key].copy()) for key in ('board', 'metadata', 'mask', 'targets'))
  if config['observation_version'] != OBS_VERSION:
    raise ValueError('Incompatible demonstration observation version')
  return config, tensors


def metrics(model, data):
  board, meta, mask, target = data
  total_loss = correct = count = 0
  with torch.no_grad():
    for indices in torch.arange(len(board)).split(256):
      dist, _ = model(board[indices].float(), meta[indices], mask[indices])
      total_loss += (-(target[indices] * dist.logits).sum(-1)).sum().item()
      predicted = dist.logits.argmax(-1)
      correct += (target[indices].gather(1, predicted[:, None]) > 0).sum().item()
      count += len(indices)
  return {'cross_entropy': total_loss / count, 'teacher_agreement': correct / count}


def clone(args):
  config, train_data = read_dataset(args.dataset)
  validation_config, validation_data = read_dataset(args.validation)
  if set(config['seeds']) & set(validation_config['seeds']):
    raise ValueError('Demonstration train/validation episodes overlap')
  for key in ('objective', 'max_pieces', 'color_profile', 'observation_version', 'engine_sha256'):
    if config[key] != validation_config[key]:
      raise ValueError(f'Demonstration task mismatch: {key}')
  torch.manual_seed(args.seed)
  model = ActorCritic()
  optimizer = torch.optim.Adam(model.parameters(), lr=3e-4)
  board, meta, mask, target = train_data
  history = []
  best = float('inf')
  for epoch in range(args.epochs):
    model.train()
    for indices in torch.randperm(len(board)).split(getattr(args, 'batch_size', 128)):
      dist, _ = model(board[indices].float(), meta[indices], mask[indices])
      loss = -(target[indices] * dist.logits).sum(-1).mean()
      optimizer.zero_grad()
      loss.backward()
      torch.nn.utils.clip_grad_norm_(model.parameters(), 0.5)
      optimizer.step()
    model.eval()
    validation = metrics(model, validation_data)
    history.append({'epoch': epoch + 1, 'validation': validation})
    print(json.dumps(history[-1]), flush=True)
    if validation['cross_entropy'] < best:
      best = validation['cross_entropy']
      metadata = {'observation_version': OBS_VERSION, 'objective': config['objective'],
        'color_profile': config['color_profile'], 'max_pieces': config['max_pieces'],
        'training_seed': args.seed, 'training_seeds': config['seeds'],
        'validation_seeds': validation_config['seeds'], 'steps': 0, 'algorithm': 'behavior_cloning',
        'demonstration_steps': len(board), 'epoch': epoch + 1,
        'batch_size': getattr(args, 'batch_size', 128),
        'dataset_sha256': hashlib.sha256(args.dataset.read_bytes()).hexdigest(),
        'validation_sha256': hashlib.sha256(args.validation.read_bytes()).hexdigest(),
        'source_sha256': source_hash(), 'engine_sha256': config['engine_sha256'],
        'torch_version': str(torch.__version__)}
      args.model.parent.mkdir(parents=True, exist_ok=True)
      torch.save({'metadata': metadata, 'weights': model.state_dict()}, args.model)
  save(args.model.with_suffix('.json'), {'history': history, 'best_validation_cross_entropy': best,
    'dataset': str(args.dataset), 'validation': str(args.validation)})


def main():
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument('mode', choices=['collect', 'train'])
  parser.add_argument('--dataset', type=Path, required=True)
  parser.add_argument('--validation', type=Path)
  parser.add_argument('--model', type=Path)
  parser.add_argument('--seed', type=int, default=1000000)
  parser.add_argument('--steps', type=int, default=12000)
  parser.add_argument('--max-pieces', type=int, default=300)
  parser.add_argument('--color-profile', choices=['classic', 'rare_six', 'rare_seven'], default='rare_seven')
  parser.add_argument('--epochs', type=int, default=15)
  parser.add_argument('--threads', type=int, default=2)
  parser.add_argument('--batch-size', type=int, default=128)
  args = parser.parse_args()
  if min(args.steps, args.max_pieces, args.epochs, args.threads, args.batch_size) < 1 or not 0 <= args.seed <= 2**31 - args.steps:
    parser.error('Require positive budgets and nonnegative Int32 seeds')
  torch.set_num_threads(args.threads)
  if args.mode == 'collect':
    if args.dataset.suffix != '.npz':
      parser.error('Dataset must end in .npz')
    collect(args.dataset, args.seed, args.steps, args.max_pieces, args.color_profile)
  else:
    if args.model is None or args.validation is None:
      parser.error('Training requires --model and --validation')
    clone(args)


if __name__ == '__main__':
  main()
