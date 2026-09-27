"""共享规则结算候选棋盘，再学习候选排序；首轮采用教师行为克隆。"""
import argparse
import hashlib
import json
from pathlib import Path
import statistics
import time

import numpy as np
import torch
from torch import nn

from .engine import ENGINE
from .imitation import teacher_targets
from .neural import ActorCritic, encode
from .rl_env import PlacementEnv
from .train import save, source_hash

VERSION = 1


class CandidateRanker(nn.Module):
  def __init__(self):
    super().__init__()
    backbone = ActorCritic()
    self.board = backbone.board
    self.metadata = nn.Sequential(nn.Linear(77, 64), nn.ReLU())
    self.rank = nn.Sequential(nn.Linear(192, 128), nn.ReLU(), nn.Linear(128, 1))

  def forward(self, boards, metadata, mask):
    # Evaluate only valid candidates; padded boards never enter the network.
    features = torch.cat([self.board(boards[mask].float()), self.metadata(metadata[mask])], -1)
    scores = boards.new_full(mask.shape, -1e9, dtype=torch.float32)
    scores[mask] = self.rank(features).squeeze(-1)
    return scores


def candidates(env):
  response = env.engine.command('afterstates')
  boards = np.zeros((40, 9, 18, 10), dtype=np.uint8)
  metadata = np.zeros((40, 77), dtype=np.float32)
  mask = np.zeros(40, dtype=bool)
  for option in response['actions']:
    state = option['state']
    if state is None:
      continue
    action = option['action']
    board, meta = encode(state, env.objective, max(0, 1 - state['locked'] / env.max_pieces))
    boards[action] = board
    metadata[action, :73] = meta
    metadata[action, 73:] = [(state['score'] - env.state['score']) / 10000,
      (state['cleared'] - env.state['cleared']) / 10, state['is_finished'],
      state['best_chain'] / 10]
    mask[action] = True
  if not mask.any():
    raise RuntimeError('No legal candidate')
  return boards, metadata, mask


def collect(path, seed, steps):
  observations, originals, targets, seeds, episodes = [], [], [], [], []
  with PlacementEnv('survival') as env:
    while len(observations) < steps:
      current_seed = seed + len(seeds)
      seeds.append(current_seed)
      observation = env.reset(current_seed)
      while True:
        originals.append(observation)
        observations.append(candidates(env))
        target = teacher_targets(env.state, env.actions)
        targets.append(target)
        observation, _, done, info = env.step(target.argmax())
        if done:
          episodes.append(info)
          break
  config = {'version': VERSION, 'seeds': seeds, 'steps': len(observations), 'episodes': episodes,
    'teacher': 'legal_placement_baseline', 'objective': 'survival', 'max_pieces': 300,
    'color_profile': 'rare_seven', 'engine_sha256': hashlib.sha256(ENGINE.read_bytes()).hexdigest()}
  path.parent.mkdir(parents=True, exist_ok=True)
  np.savez_compressed(path, boards=np.stack([o[0] for o in observations]),
    metadata=np.stack([o[1] for o in observations]), mask=np.stack([o[2] for o in observations]),
    targets=np.stack(targets), config=json.dumps(config))
  from .neural import OBS_VERSION
  direct_config = {**config, 'observation_version': OBS_VERSION}
  np.savez_compressed(path.with_name(path.stem + '-direct.npz'),
    board=np.stack([o[0] for o in originals]).astype(np.uint8),
    metadata=np.stack([o[1] for o in originals]), mask=np.stack([o[2] for o in originals]),
    targets=np.stack(targets), config=json.dumps(direct_config))
  print(json.dumps({'dataset': str(path), 'steps': len(observations)}), flush=True)


def dataset(path):
  with np.load(path, allow_pickle=False) as data:
    config = json.loads(str(data['config']))
    tensors = tuple(torch.from_numpy(data[k].copy()) for k in ('boards', 'metadata', 'mask', 'targets'))
  if config['version'] != VERSION:
    raise ValueError('Incompatible candidate dataset')
  return config, tensors


def metrics(model, data):
  loss = correct = 0
  with torch.no_grad():
    for indices in torch.arange(len(data[0])).split(8):
      boards, meta, mask, targets = (t[indices] for t in data)
      scores = model(boards, meta, mask)
      loss += (-(targets * scores.log_softmax(-1)).sum(-1)).sum().item()
      correct += (targets.gather(1, scores.argmax(-1)[:, None]) > 0).sum().item()
  return {'cross_entropy': loss / len(data[0]), 'teacher_agreement': correct / len(data[0])}


def train(args):
  config, data = dataset(args.dataset)
  validation_config, validation = dataset(args.validation)
  if set(config['seeds']) & set(validation_config['seeds']):
    raise ValueError('Train and validation episodes overlap')
  for key in ('engine_sha256', 'objective', 'max_pieces', 'color_profile'):
    if config[key] != validation_config[key]:
      raise ValueError(f'Task mismatch: {key}')
  torch.manual_seed(args.seed)
  model = CandidateRanker()
  optimizer = torch.optim.Adam(model.parameters(), lr=3e-4)
  history, best = [], float('inf')
  for epoch in range(args.epochs):
    for indices in torch.randperm(len(data[0])).split(8):
      boards, meta, mask, targets = (t[indices] for t in data)
      scores = model(boards, meta, mask)
      loss = -(targets * scores.log_softmax(-1)).sum(-1).mean()
      optimizer.zero_grad()
      loss.backward()
      nn.utils.clip_grad_norm_(model.parameters(), 0.5)
      optimizer.step()
    result = {'epoch': epoch + 1, 'validation': metrics(model, validation)}
    history.append(result)
    print(json.dumps(result), flush=True)
    if result['validation']['cross_entropy'] < best:
      best = result['validation']['cross_entropy']
      metadata = {'version': VERSION, 'algorithm': 'afterstate_behavior_cloning',
        'training_seeds': config['seeds'], 'validation_seeds': validation_config['seeds'],
        'parameters': sum(p.numel() for p in model.parameters()), 'seed': args.seed,
        'epoch': epoch + 1, 'source_sha256': source_hash(), 'engine_sha256': config['engine_sha256'],
        'dataset_sha256': hashlib.sha256(args.dataset.read_bytes()).hexdigest(),
        'validation_sha256': hashlib.sha256(args.validation.read_bytes()).hexdigest()}
      args.model.parent.mkdir(parents=True, exist_ok=True)
      torch.save({'metadata': metadata, 'weights': model.state_dict()}, args.model)
  save(args.model.with_suffix('.json'), {'history': history, 'config': config,
    'validation_config': validation_config})


def load_ranker(path):
  checkpoint = torch.load(path, map_location='cpu', weights_only=True)
  metadata = checkpoint['metadata']
  if metadata['version'] != VERSION or metadata['algorithm'] != 'afterstate_behavior_cloning':
    raise ValueError('Incompatible candidate checkpoint')
  model = CandidateRanker()
  model.load_state_dict(checkpoint['weights'])
  model.eval()
  return model, metadata


def play(args):
  from .trajectory import replay
  model, _ = load_ranker(args.model)
  args.trace.parent.mkdir(parents=True, exist_ok=True)
  with args.trace.open('x', encoding='utf-8') as trace, PlacementEnv('survival', trace=trace) as env:
    env.reset(args.seed)
    with torch.no_grad():
      while True:
        data = tuple(torch.from_numpy(x)[None] for x in candidates(env))
        action = int(model(*data).argmax(-1).item())
        _, _, done, info = env.step(action)
        if done:
          break
  verified = replay(args.trace)
  print(json.dumps({'result': info, 'verified': verified['verified'], 'replay_steps': verified['steps']}))


def evaluate(args):
  from .ppo import load_model, tensors
  model, metadata = load_ranker(args.model)
  seeds = list(range(args.seed, args.seed + args.episodes))
  warm, warm_meta = load_model(args.baseline)
  direct, direct_meta = load_model(args.direct)
  for task in (warm_meta, direct_meta):
    if (task['objective'], task['max_pieces'], task['color_profile']) != ('survival', 300, 'rare_seven'):
      raise ValueError('Comparison requires survival, 300 pieces and rare_seven')
  excluded = set(metadata['training_seeds'] + metadata['validation_seeds'] +
    warm_meta['training_seeds'] + warm_meta.get('validation_seeds', []) +
    direct_meta['training_seeds'] + direct_meta.get('validation_seeds', []))
  if excluded & set(seeds):
    raise ValueError('Evaluation seeds overlap training or validation')
  runs = {}
  with torch.no_grad():
    for policy in ('teacher', 'warm_ppo', 'direct_bc', 'afterstate'):
      episodes = []
      with PlacementEnv('survival') as env:
        for seed in seeds:
          observation = env.reset(seed)
          inference_seconds = 0.0
          while True:
            started = time.perf_counter()
            if policy == 'teacher':
              action = int(teacher_targets(env.state, env.actions).argmax())
            elif policy in ('warm_ppo', 'direct_bc'):
              policy_model = warm if policy == 'warm_ppo' else direct
              dist, _ = policy_model(*tensors([observation]))
              action = int(dist.logits.argmax(-1).item())
            else:
              data = tuple(torch.from_numpy(x)[None] for x in candidates(env))
              action = int(model(*data).argmax(-1).item())
            inference_seconds += time.perf_counter() - started
            observation, _, done, info = env.step(action)
            if done:
              episodes.append({**info, 'decision_seconds': inference_seconds})
              break
      runs[policy] = {'episodes': episodes, 'mean_locked': statistics.mean(e['locked'] for e in episodes),
        'median_locked': statistics.median(e['locked'] for e in episodes),
        'mean_score': statistics.mean(e['score'] for e in episodes),
        'decision_ms_per_piece': 1000 * sum(e['decision_seconds'] for e in episodes) / sum(e['locked'] for e in episodes)}
      print(json.dumps({'policy': policy, **{k: v for k, v in runs[policy].items() if k != 'episodes'}}), flush=True)
  save(args.output, {'seeds': seeds, 'checkpoint': metadata, 'baseline_metadata': warm_meta, 'direct_metadata': direct_meta,
    'model_sha256': hashlib.sha256(args.model.read_bytes()).hexdigest(),
    'baseline_sha256': hashlib.sha256(args.baseline.read_bytes()).hexdigest(),
    'direct_sha256': hashlib.sha256(args.direct.read_bytes()).hexdigest(),
    'candidate_training': json.loads(args.model.with_suffix('.json').read_text()),
    'direct_training': json.loads(args.direct.with_suffix('.json').read_text()),
    'engine_sha256': hashlib.sha256(ENGINE.read_bytes()).hexdigest(), 'runs': runs})


def main():
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument('mode', choices=['collect', 'train', 'evaluate', 'play'])
  parser.add_argument('--dataset', type=Path)
  parser.add_argument('--validation', type=Path)
  parser.add_argument('--model', type=Path)
  parser.add_argument('--baseline', type=Path)
  parser.add_argument('--direct', type=Path)
  parser.add_argument('--output', type=Path)
  parser.add_argument('--trace', type=Path)
  parser.add_argument('--seed', type=int, default=42)
  parser.add_argument('--steps', type=int, default=2000)
  parser.add_argument('--epochs', type=int, default=15)
  parser.add_argument('--episodes', type=int, default=30)
  args = parser.parse_args()
  if min(args.steps, args.epochs, args.episodes) < 1 or not 0 <= args.seed <= 2**31 - max(args.steps, args.episodes):
    parser.error('Require positive budgets and valid seed range')
  required = {'collect': ('dataset',), 'train': ('dataset', 'validation', 'model'),
    'evaluate': ('model', 'baseline', 'direct', 'output'), 'play': ('model', 'trace')}
  if any(getattr(args, key) is None for key in required[args.mode]):
    parser.error(f'{args.mode} requires {required[args.mode]}')
  torch.set_num_threads(2)
  if args.mode == 'collect':
    if args.dataset.suffix != '.npz':
      parser.error('Dataset must end in .npz')
    collect(args.dataset, args.seed, args.steps)
  elif args.mode == 'train':
    train(args)
  elif args.mode == 'evaluate':
    evaluate(args)
  else:
    play(args)


if __name__ == '__main__':
  main()
