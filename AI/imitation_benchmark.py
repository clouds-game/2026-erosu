"""固定独立种子比较合法教师、随机、行为克隆和两种 PPO 初始化。"""

import argparse
import hashlib
import json
from pathlib import Path
import statistics

import numpy as np
import torch

from .engine import ENGINE
from .imitation import teacher_targets
from .ppo import load_model, tensors
from .rl_env import PlacementEnv
from .train import save


def benchmark_policy(path, kind, seeds, sample=False):
  model, metadata = load_model(path) if path else (None, None)
  if metadata:
    if set(seeds) & (set(metadata['training_seeds']) | set(metadata.get('validation_seeds', []))):
      raise ValueError('Benchmark seeds overlap training/model-selection data')
    if any(metadata[key] != value for key, value in
        [('objective', 'survival'), ('max_pieces', 300), ('color_profile', 'rare_seven')]):
      raise ValueError('Benchmark requires seven-color survival with 300-piece budget')
  runs = []
  with PlacementEnv('survival', 300, 'rare_seven') as env:
    for seed in seeds:
      torch.manual_seed(seed)
      rng = np.random.default_rng(seed)
      observation = env.reset(seed)
      gold_placed = 0
      while True:
        if kind == 'teacher':
          action = teacher_targets(env.state, env.actions).argmax()
        elif kind == 'random':
          action = rng.choice(np.flatnonzero(observation[2]))
        else:
          with torch.no_grad():
            distribution, _ = model(*tensors([observation]))
            action = (distribution.sample() if sample else distribution.logits.argmax(-1)).item()
        gold_placed += env.state['active']['color'] == 6
        observation, _, done, info = env.step(action)
        if done:
          info['gold_cleared'] = gold_placed - sum(piece['color'] == 6 for piece in env.state['pieces'])
          runs.append(info)
          break
  summary = {'mean_locked': statistics.mean(r['locked'] for r in runs),
    'median_locked': statistics.median(r['locked'] for r in runs),
    'mean_score': statistics.mean(r['score'] for r in runs),
    'clears_per_placed_piece': sum(r['cleared'] for r in runs) / sum(r['locked'] for r in runs),
    'gold_clear_episodes': sum(r['gold_cleared'] > 0 for r in runs),
    'capped': sum(r['capped'] for r in runs)}
  return {'summary': summary, 'runs': runs, 'model_metadata': metadata,
    'model_sha256': hashlib.sha256(path.read_bytes()).hexdigest() if path else None}


def main():
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument('--directory', type=Path, default=Path('build/ai/imitation'))
  parser.add_argument('--seed', type=int, default=5000000)
  parser.add_argument('--episodes', type=int, default=30)
  parser.add_argument('--report', type=Path, default=Path('build/ai/imitation/benchmark.json'))
  args = parser.parse_args()
  if args.episodes < 1 or not 0 <= args.seed <= 2**31 - args.episodes:
    parser.error('Require positive episode count and Int32 seeds')
  torch.set_num_threads(2)
  seeds = list(range(args.seed, args.seed + args.episodes))
  for kind in ('bc', 'scratch', 'warm'):
    for training_seed in (42, 43, 44):
      _, metadata = load_model(args.directory / f'{kind}-{training_seed}.pt')
      if metadata['training_seed'] != training_seed:
        raise ValueError('Checkpoint training seed does not match experiment slot')
      if metadata['steps'] != (0 if kind == 'bc' else 32768):
        raise ValueError('Benchmark requires completed fixed-budget checkpoints')
  report = {'complete': False, 'seeds': seeds, 'training_seeds': [42, 43, 44], 'ppo_steps_per_model': 32768,
    'engine_sha256': hashlib.sha256(ENGINE.read_bytes()).hexdigest(), 'results': {}}
  for kind in ('teacher', 'random', 'bc', 'scratch', 'warm'):
    for training_seed in ([None] if kind in ('teacher', 'random') else (42, 43, 44)):
      for sample in ([False] if training_seed is None else [False, True]):
        path = None if training_seed is None else args.directory / f'{kind}-{training_seed}.pt'
        name = kind if path is None else f'{kind}-{training_seed}-' + ('sample' if sample else 'greedy')
        result = benchmark_policy(path, kind, seeds, sample)
        report['results'][name] = result
        save(args.report, report)
        print(json.dumps({'policy': name, **result['summary']}), flush=True)
  report['complete'] = True
  save(args.report, report)


if __name__ == '__main__':
  main()
