"""候选模型的 DAgger 与配对教师回放控制实验。"""
import argparse
import hashlib
import json
from pathlib import Path
import statistics
import time

import numpy as np
import torch

from .afterstate import VERSION, candidates, dataset, load_ranker, metrics
from .engine import ENGINE
from .imitation import teacher_targets
from .rl_env import PlacementEnv
from .train import save, source_hash

TASK = {'version': VERSION, 'objective': 'survival', 'max_pieces': 300, 'color_profile': 'rare_seven'}


def fingerprint(path):
  return hashlib.sha256(path.read_bytes()).hexdigest()


def validate_task(config):
  if any(config[key] != value for key, value in TASK.items()):
    raise ValueError('DAgger requires the current seven-color survival candidate task')
  if config['engine_sha256'] != fingerprint(ENGINE):
    raise ValueError('Dataset and current engine differ')


def collect_rollouts(model, path, seed, steps, reserved, collector_hash=None):
  """教师标注所有访问状态；执行模型动作，不用标签替换模型动作。"""
  observations, targets, actions, seeds, episodes = [], [], [], [], []
  engine_hash = fingerprint(ENGINE)
  if model is not None:
    model.eval()
  with PlacementEnv('survival') as env, torch.no_grad():
    while len(observations) < steps:
      current_seed = seed + len(seeds)
      if fingerprint(ENGINE) != engine_hash:
        raise ValueError('Engine binary changed during collection')
      if current_seed in reserved:
        raise ValueError('Collection seeds overlap parent training/validation data')
      seeds.append(current_seed)
      env.reset(current_seed)
      while True:
        observation = candidates(env)
        target = teacher_targets(env.state, env.actions)
        action = int(target.argmax()) if model is None else int(model(
          *(torch.from_numpy(t)[None] for t in observation)).argmax(-1).item())
        observations.append(observation)
        targets.append(target)
        actions.append(action)
        _, _, done, info = env.step(action)
        if done:
          episodes.append(info)
          break
  if fingerprint(ENGINE) != engine_hash:
    raise ValueError('Engine binary changed during collection')
  # Finish the last episode, but keep exactly the requested label budget in both arms.
  kept = observations[:steps]
  kept_targets = np.stack(targets[:steps])
  executed = np.array(actions[:steps], dtype=np.int64)
  config = {**TASK, 'engine_sha256': engine_hash, 'seeds': seeds, 'steps': steps,
    'collected_steps': len(observations), 'episodes': episodes,
    'collector': 'teacher' if model is None else 'learner_greedy', 'collector_sha256': collector_hash,
    'teacher': 'legal_placement_baseline',
    'teacher_disagreement': float((kept_targets[np.arange(steps), executed] == 0).mean())}
  np.savez_compressed(path, boards=np.stack([o[0] for o in kept]),
    metadata=np.stack([o[1] for o in kept]), mask=np.stack([o[2] for o in kept]),
    targets=kept_targets, executed_actions=executed, config=json.dumps(config))
  return config


def optimize(model, optimizer, base, aggregate, updates, generator):
  """每个八样本批次，一半原始示范，一半累计新增示范。"""
  model.train()
  total = 0.0
  for _ in range(updates):
    old = torch.randint(len(base[0]), (4,), generator=generator)
    new = torch.randint(len(aggregate[0]), (4,), generator=generator)
    boards, meta, mask, targets = (torch.cat([a[old], b[new]]) for a, b in zip(base, aggregate))
    scores = model(boards, meta, mask)
    loss = -(targets * scores.log_softmax(-1)).sum(-1).mean()
    optimizer.zero_grad()
    loss.backward()
    torch.nn.utils.clip_grad_norm_(model.parameters(), 0.5)
    optimizer.step()
    total += loss.item()
  model.eval()
  return total / updates


def collection_slots(steps, rounds):
  stride = max(1000, steps + 1)
  return stride, max(10000, rounds * stride)


def train_experiment(args):
  base_config, base = dataset(args.dataset)
  validation_config, validation = dataset(args.validation)
  validate_task(base_config)
  validate_task(validation_config)
  _, parent_meta = load_ranker(args.parent)
  if parent_meta['engine_sha256'] != fingerprint(ENGINE):
    raise ValueError('Parent and current engine differ')
  parent_training = set(parent_meta['training_seeds']) | set(base_config['seeds'])
  parent_validation = set(parent_meta['validation_seeds']) | set(validation_config['seeds'])
  if parent_training & parent_validation:
    raise ValueError('Training and validation episodes overlap')
  if args.directory.exists() and any(args.directory.iterdir()):
    raise FileExistsError('Experiment output directory must be empty')
  args.directory.mkdir(parents=True, exist_ok=True)
  experiment = {'complete': False, 'parent': str(args.parent), 'parent_sha256': fingerprint(args.parent),
    'parent_metadata': parent_meta, 'dataset_sha256': fingerprint(args.dataset),
    'validation_sha256': fingerprint(args.validation), 'source_sha256': source_hash(),
    'engine_sha256': fingerprint(ENGINE), 'fine_tuning_seeds': args.seeds,
    'rounds': args.rounds, 'labels_per_round': args.steps, 'updates_per_round': args.updates,
    'batch_size': 8, 'base_fraction': 0.5, 'learning_rate': 3e-4,
    'collection_seed': args.collection_seed, 'models': {}}
  manifest = args.directory / 'experiment.json'
  save(manifest, experiment)
  for index, training_seed in enumerate(args.seeds):
    for arm in ('teacher_replay', 'dagger'):
      model, _ = load_ranker(args.parent)
      optimizer = torch.optim.Adam(model.parameters(), lr=3e-4)
      generator = torch.Generator().manual_seed(training_seed)
      aggregate = None
      training_seeds = set(parent_training)
      history = []
      collector_path = args.parent
      for round_index in range(args.rounds):
        if fingerprint(ENGINE) != experiment['engine_sha256']:
          raise ValueError('Engine binary changed during training experiment')
        name = f'{arm}-{training_seed}-round-{round_index + 1}'
        path = args.directory / (name + '.npz')
        # Arms share starting episode seeds; different policies visit different boards.
        stride, cohort_stride = collection_slots(args.steps, args.rounds)
        seed = args.collection_seed + index * cohort_stride + round_index * stride
        config = collect_rollouts(model if arm == 'dagger' else None, path, seed, args.steps,
          training_seeds | parent_validation, fingerprint(collector_path) if arm == 'dagger' else None)
        training_seeds.update(config['seeds'])
        _, data = dataset(path)
        aggregate = data if aggregate is None else tuple(torch.cat([a, b]) for a, b in zip(aggregate, data))
        loss = optimize(model, optimizer, base, aggregate, args.updates, generator)
        if fingerprint(ENGINE) != experiment['engine_sha256']:
          raise ValueError('Engine binary changed during training experiment')
        result = {'round': round_index + 1, 'loss': loss, 'collection': config,
          'validation': metrics(model, validation), 'aggregate_labels': len(aggregate[0])}
        history.append(result)
        metadata = {**parent_meta, 'algorithm': 'afterstate_' + arm, 'training_seed': training_seed,
          'training_seeds': sorted(training_seeds), 'validation_seeds': sorted(parent_validation),
          'parent_sha256': experiment['parent_sha256'], 'round': round_index + 1,
          'fine_tuning_updates': (round_index + 1) * args.updates,
          'new_labels': len(aggregate[0]), 'source_sha256': experiment['source_sha256'],
          'checkpoint_selection': 'fixed_final_update_no_validation_selection'}
        collector_path = args.directory / (name + '.pt')
        torch.save({'metadata': metadata, 'weights': model.state_dict()}, collector_path)
        save(collector_path.with_suffix('.json'), {'metadata': metadata, 'history': history})
        print(json.dumps({'arm': arm, 'training_seed': training_seed, **result}), flush=True)
      experiment['models'][f'{arm}-{training_seed}'] = {
        'path': str(collector_path), 'sha256': fingerprint(collector_path), 'metadata': metadata,
        'history': history}
      save(manifest, experiment)
  if fingerprint(ENGINE) != experiment['engine_sha256']:
    raise ValueError('Engine binary changed during training experiment')
  experiment['complete'] = True
  save(manifest, experiment)


def benchmark_policy(path, seeds):
  model, metadata = load_ranker(path) if path else (None, None)
  if metadata and set(seeds) & set(metadata['training_seeds'] + metadata['validation_seeds']):
    raise ValueError('Evaluation seeds overlap training or validation data')
  engine_hash = fingerprint(ENGINE)
  runs = []
  with PlacementEnv('survival') as env, torch.no_grad():
    for seed in seeds:
      if fingerprint(ENGINE) != engine_hash:
        raise ValueError('Engine binary changed during evaluation')
      env.reset(seed)
      placed = [0] * 7
      decision_seconds = 0.0
      while True:
        started = time.perf_counter()
        if model is None:
          action = int(teacher_targets(env.state, env.actions).argmax())
        else:
          observation = candidates(env)
          action = int(model(*(torch.from_numpy(t)[None] for t in observation)).argmax(-1).item())
        decision_seconds += time.perf_counter() - started
        placed[env.state['active']['color']] += 1
        _, _, done, info = env.step(action)
        if done:
          remaining = [sum(p['color'] == color for p in env.state['pieces']) for color in range(7)]
          cleared = [a - b for a, b in zip(placed, remaining)]
          runs.append({**info, 'placed_by_color': placed, 'cleared_by_color': cleared,
            'gold_cleared': cleared[6], 'decision_seconds': decision_seconds})
          break
  if fingerprint(ENGINE) != engine_hash:
    raise ValueError('Engine binary changed during evaluation')
  locked = [r['locked'] for r in runs]
  total_locked = sum(locked)
  summary = {'mean_locked': statistics.mean(locked), 'median_locked': statistics.median(locked),
    'p25_locked': float(np.percentile(locked, 25)), 'p75_locked': float(np.percentile(locked, 75)),
    'min_locked': min(locked), 'max_locked': max(locked),
    'mean_score': statistics.mean(r['score'] for r in runs),
    'clears_per_placed_piece': sum(r['cleared'] for r in runs) / total_locked,
    'gold_clear_episodes': sum(r['gold_cleared'] > 0 for r in runs),
    'capped': sum(r['capped'] for r in runs),
    'decision_ms_per_piece': 1000 * sum(r['decision_seconds'] for r in runs) / total_locked}
  return {'metadata': metadata, 'model_sha256': fingerprint(path) if path else None,
    'summary': summary, 'runs': runs}


def evaluate_experiment(args):
  experiment = json.loads((args.directory / 'experiment.json').read_text())
  if not experiment['complete']:
    raise ValueError('Training experiment is incomplete')
  if experiment['engine_sha256'] != fingerprint(ENGINE):
    raise ValueError('Experiment and evaluation engine differ')
  seeds = list(range(args.seed, args.seed + args.episodes))
  models = {'teacher': None, 'initial': Path(experiment['parent'])}
  for name, info in experiment['models'].items():
    path = Path(info['path'])
    if fingerprint(path) != info['sha256']:
      raise ValueError('Experiment checkpoint changed')
    models[name] = path
  if fingerprint(models['initial']) != experiment['parent_sha256']:
    raise ValueError('Initial checkpoint changed')
  protected = {p.resolve() for p in models.values() if p}
  protected.update(p.with_suffix('.json').resolve() for p in models.values() if p)
  protected.add((args.directory / 'experiment.json').resolve())
  if args.report.suffix != '.json' or args.report.resolve() in protected:
    raise ValueError('Report must be JSON and cannot overwrite experiment inputs')
  # Check every model before executing any benchmark game.
  for path in models.values():
    if path:
      _, metadata = load_ranker(path)
      if set(seeds) & set(metadata['training_seeds'] + metadata['validation_seeds']):
        raise ValueError('Evaluation seeds overlap training or validation data')
  report = {'complete': False, 'seeds': seeds, 'experiment': experiment, 'results': {}}
  for name, path in models.items():
    if fingerprint(ENGINE) != experiment['engine_sha256']:
      raise ValueError('Engine binary changed during evaluation experiment')
    result = benchmark_policy(path, seeds)
    report['results'][name] = result
    save(args.report, report)
    print(json.dumps({'policy': name, **result['summary']}), flush=True)
  if fingerprint(ENGINE) != experiment['engine_sha256']:
    raise ValueError('Engine binary changed during evaluation experiment')
  report['aggregates'] = {}
  for arm in ('teacher_replay', 'dagger'):
    settings = [report['results'][f'{arm}-{seed}']['summary'] for seed in experiment['fine_tuning_seeds']]
    report['aggregates'][arm] = {
      key: statistics.mean(s[key] for s in settings) for key in ('mean_locked', 'mean_score', 'clears_per_placed_piece')}
  report['complete'] = True
  save(args.report, report)


def main():
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument('mode', choices=['train', 'evaluate'])
  parser.add_argument('--parent', type=Path)
  parser.add_argument('--dataset', type=Path)
  parser.add_argument('--validation', type=Path)
  parser.add_argument('--directory', type=Path, required=True)
  parser.add_argument('--seeds', type=int, nargs='+', default=[42, 43, 44])
  parser.add_argument('--collection-seed', type=int, default=7000000)
  parser.add_argument('--rounds', type=int, default=2)
  parser.add_argument('--steps', type=int, default=600)
  parser.add_argument('--updates', type=int, default=100)
  parser.add_argument('--seed', type=int, default=9000000)
  parser.add_argument('--episodes', type=int, default=50)
  parser.add_argument('--report', type=Path)
  args = parser.parse_args()
  if min(args.rounds, args.steps, args.updates, args.episodes) < 1:
    parser.error('Require positive budgets')
  if len(args.seeds) != len(set(args.seeds)) or any(not 0 <= seed <= 2**63 - 1 for seed in args.seeds):
    parser.error('Fine-tuning seeds must be unique and fit torch generator range')
  _, cohort_stride = collection_slots(args.steps, args.rounds)
  if not 0 <= args.collection_seed <= 2**31 - len(args.seeds) * cohort_stride:
    parser.error('Collection seed slots must fit nonnegative Int32')
  if not 0 <= args.seed <= 2**31 - args.episodes:
    parser.error('Evaluation seeds must fit nonnegative Int32')
  torch.set_num_threads(2)
  required = ('parent', 'dataset', 'validation') if args.mode == 'train' else ('report',)
  if any(getattr(args, key) is None for key in required):
    parser.error(f'{args.mode} requires {required}')
  if args.mode == 'train':
    train_experiment(args)
  else:
    evaluate_experiment(args)


if __name__ == '__main__':
  main()
