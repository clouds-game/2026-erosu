"""导出同种子、十二种规则组合的 CNN 训练前后已验证回放。"""
import argparse
import hashlib
import json
from pathlib import Path

import torch

from .compare import frames, write_viewer
from .engine import ENGINE
from .modes import GameMode
from .neural import FEATURE_OBS_VERSION
from .ppo import checkpoint_modes, load_model, tensors
from .rl_env import PlacementEnv
from .train import save
from .trajectory import read, replay


def fingerprint(path):
  return hashlib.sha256(path.read_bytes()).hexdigest()


def mode_label(mode):
  profile = {'classic': 'Classic · 5 colors', 'rare_six': 'Rare · 6 colors',
    'rare_seven': 'Rare · 7 colors'}[mode.color_profile]
  features = [label for enabled, label in [(mode.anchored_blocks, 'fixed squares'),
    (mode.enclosed_fill, 'enclosed fill')] if enabled]
  return ' · '.join([profile, *features])


def record_policy(model, metadata, mode, seed, path):
  with path.open('x', encoding='utf-8') as trace, PlacementEnv(metadata['objective'],
      metadata['max_pieces'], mode.color_profile, trace, metadata['observation_version']) as env:
    observation = env.reset(seed, mode)
    while True:
      with torch.no_grad():
        distribution, _ = model(*tensors([observation]))
        action = distribution.logits.argmax(-1).item()
      observation, _, done, result = env.step(action)
      if done:
        result = {**result, 'best_chain': env.state['best_chain']}
        break
  verified = replay(path)
  _, steps = read(path)
  return {'result': result, 'verified_steps': verified['steps'], 'turns': frames(steps)}, \
    steps[0]['response']['state']['colors']


def build_demo(report_path, baseline_path, selected_path, output, seeds=None):
  if output.exists() and (not output.is_dir() or any(output.iterdir())):
    raise FileExistsError('Demo output directory must be empty')
  report = json.loads(report_path.read_text())
  if not report['complete'] or report['action_selection'] != 'greedy':
    raise ValueError('Demo requires a completed greedy evaluation')
  engine_hash = fingerprint(ENGINE)
  if report['engine_sha256'] != engine_hash:
    raise ValueError('Demo engine differs from benchmark; use the frozen CHROMA_ENGINE build')
  seeds = list(seeds) if seeds is not None else report['test']['seeds'][:1]
  if not seeds or len(set(seeds)) != len(seeds) or any(not 0 <= seed <= 2**31 - 1 for seed in seeds):
    raise ValueError('Require unique nonnegative Int32 seeds')
  if not set(seeds) <= set(report['test']['seeds']):
    raise ValueError('Demo seeds must come from the held-out benchmark')
  modes = [GameMode(**options) for options in report['game_modes']]
  if not modes or len(set(modes)) != len(modes):
    raise ValueError('Benchmark must contain distinct game modes')
  models = []
  for key, name, path in [('baseline', 'Previous shared CNN', baseline_path),
      ('selected', 'Improved shared CNN', selected_path)]:
    model_hash = fingerprint(path)
    if model_hash != report['test'][key]['model_sha256']:
      raise ValueError(f'Checkpoint differs from benchmark: {key}')
    model, metadata = load_model(path)
    if metadata['engine_sha256'] != engine_hash or metadata['observation_version'] != FEATURE_OBS_VERSION:
      raise ValueError(f'Checkpoint engine/observation mismatch: {key}')
    if set(checkpoint_modes(metadata)) != set(modes):
      raise ValueError(f'Checkpoint game modes differ from benchmark: {key}')
    for field in ('objective', 'max_pieces'):
      if metadata[field] != report[field]:
        raise ValueError(f'Checkpoint task mismatch: {key}/{field}')
    if set(seeds) & set(metadata['training_seeds'] + metadata.get('validation_seeds', [])):
      raise ValueError(f'Demo seeds overlap training or model-selection seeds: {key}')
    expected = {}
    for run in report['test'][key]['runs']:
      identity = (run['mode'], run['seed'])
      if identity in expected:
        raise ValueError(f'Duplicate benchmark episode: {key}/{identity}')
      expected[identity] = run
    if any((mode.key, seed) not in expected for mode in modes for seed in seeds):
      raise ValueError(f'Benchmark is missing requested episodes: {key}')
    models.append((key, name, model_hash, model, metadata, expected))
  if report.get('selected_model_sha256', models[1][2]) != models[1][2]:
    raise ValueError('Selected checkpoint differs from selection report')
  output.mkdir(parents=True, exist_ok=True)
  episodes = len(report['test']['seeds']) * len(modes)
  before, after = (report['test'][key]['summary']['mean_locked'] for key in ('baseline', 'selected'))
  data = {'title': 'Multi-mode AI · before / after', 'max_pieces': report['max_pieces'],
    'engine_sha256': engine_hash, 'scenarios': [], 'benchmark_metric': 'capped',
    'benchmark_episodes': episodes, 'benchmark': [
      {'name': name, **report['test'][key]['summary']} for key, name, *_ in models],
    'benchmark_summary': f'{episodes} held-out games · Previous {before:.1f} → improved {after:.1f} mean pieces'}
  summary = {**data, 'scenarios': []}
  for mode in modes:
    for seed in seeds:
      policies = []
      for key, name, model_hash, model, metadata, expected in models:
        if fingerprint(ENGINE) != engine_hash:
          raise ValueError('Engine changed during demo generation')
        policy, data['colors'] = record_policy(model, metadata, mode, seed,
          output / f'{mode.key}-{seed}-{key}.jsonl')
        for field in ('score', 'locked', 'capped'):
          if policy['result'][field] != expected[(mode.key, seed)][field]:
            raise ValueError(f'Demo diverges from benchmark: {key}/{mode.key}/{seed}/{field}')
        policies.append({'key': key, 'name': name, 'model_sha256': model_hash, **policy})
        print(json.dumps({'policy': key, **policy['result']}), flush=True)
      # Rule-specific random draws depend on board evolution; equal seeds do not promise equal streams.
      scenario = {'seed': seed, 'game_mode': mode.key, 'label': f'{mode_label(mode)} · seed {seed}'}
      data['scenarios'].append({**scenario, 'policies': policies})
      summary['scenarios'].append({**scenario, 'policies': [
        {key: value for key, value in policy.items() if key != 'turns'} for policy in policies]})
  if fingerprint(ENGINE) != engine_hash:
    raise ValueError('Engine changed during demo generation')
  write_viewer(output / 'index.html', data)
  save(output / 'summary.json', summary)
  return summary


def main():
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument('--report', type=Path, default=Path('AI/reports/rl-improvement.json'))
  parser.add_argument('--baseline', type=Path, default=Path('build/ai/multi-mode-20260928/policy.pt'))
  parser.add_argument('--selected', type=Path, default=Path('build/ai/rl-improvement/selected.pt'))
  parser.add_argument('--output', type=Path, default=Path('build/ai/rl-demo'))
  parser.add_argument('--seeds', type=int, nargs='+', help='Default: first held-out seed, across every mode')
  parser.add_argument('--threads', type=int, default=1)
  args = parser.parse_args()
  if args.threads < 1:
    parser.error('Threads must be positive')
  torch.set_num_threads(args.threads)
  build_demo(args.report, args.baseline, args.selected, args.output, args.seeds)


if __name__ == '__main__':
  main()
