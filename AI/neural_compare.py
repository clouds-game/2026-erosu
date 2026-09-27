"""导出候选 CNN、DAgger 与教师控制的同种子可视化回放。"""
import argparse
import json
from pathlib import Path

import torch

from .afterstate import candidates, load_ranker
from .compare import frames, write_viewer
from .dagger import fingerprint
from .engine import ENGINE
from .imitation import teacher_targets
from .rl_env import PlacementEnv
from .train import save
from .trajectory import read, replay


def record_policy(model_path, seed, path, max_pieces=300):
  model, _ = load_ranker(model_path) if model_path else (None, None)
  placed = [0] * 7
  with path.open('x', encoding='utf-8') as trace, PlacementEnv('survival', max_pieces, trace=trace) as env:
    env.reset(seed)
    with torch.no_grad():
      while True:
        if model is None:
          action = int(teacher_targets(env.state, env.actions).argmax())
        else:
          observation = candidates(env)
          action = int(model(*(torch.from_numpy(t)[None] for t in observation)).argmax(-1).item())
        placed[env.state['active']['color']] += 1
        _, _, done, result = env.step(action)
        if done:
          result = {**result, 'best_chain': env.state['best_chain'], 'gold_cleared': placed[6] -
            sum(p['color'] == 6 for p in env.state['pieces'])}
          break
  verified = replay(path)
  _, steps = read(path)
  sequence = {}
  for step in steps:
    active = step['response']['state']['active']
    if active:
      sequence[active['id']] = (active['shape'], active['color'])
  return {'result': result, 'verified_steps': verified['steps'], 'turns': frames(steps)}, sequence, steps[0]['response']['state']['colors']


def build_demo(experiment_path, report_path, output, seeds):
  experiment = json.loads(experiment_path.read_text())
  report = json.loads(report_path.read_text())
  if not experiment['complete'] or not report['complete']:
    raise ValueError('Demo requires a completed experiment and evaluation')
  if fingerprint(ENGINE) != experiment['engine_sha256'] or report['experiment']['engine_sha256'] != fingerprint(ENGINE):
    raise ValueError('Demo engine differs from experiment')
  models = [('initial', 'Candidate CNN · initial', Path(experiment['parent']))]
  for training_seed in experiment['fine_tuning_seeds']:
    for arm, label in [('dagger', 'DAgger'), ('teacher_replay', 'Teacher replay')]:
      key = f'{arm}-{training_seed}'
      models.append((key, f'{label} · {training_seed}', Path(experiment['models'][key]['path'])))
  models.append(('teacher', 'Heuristic teacher', None))
  for key, _, path in models:
    if path:
      model_hash = fingerprint(path)
      expected = experiment['parent_sha256'] if key == 'initial' else experiment['models'][key]['sha256']
      if model_hash != expected or report['results'][key]['model_sha256'] != model_hash:
        raise ValueError('Checkpoint differs from benchmark')
      _, metadata = load_ranker(path)
      if set(seeds) & set(metadata['training_seeds'] + metadata['validation_seeds']):
        raise ValueError('Demo seeds overlap training or validation data')
    if not set(seeds) <= set(report['seeds']):
      raise ValueError('Demo seeds must come from the held-out benchmark')
  if output.exists() and any(output.iterdir()):
    raise FileExistsError('Demo output directory must be empty')
  output.mkdir(parents=True, exist_ok=True)
  data = {'title': 'Candidate CNN / DAgger replay', 'max_pieces': 300, 'scenarios': [],
    'benchmark_episodes': len(report['seeds']), 'benchmark': [
      {'name': name, **report['results'][key]['summary']} for key, name, _ in models],
    'benchmark_summary': f"{len(report['seeds'])}-seed evaluation · Initial {report['results']['initial']['summary']['mean_locked']:.1f} · "
      f"DAgger {report['aggregates']['dagger']['mean_locked']:.1f} · Teacher replay {report['aggregates']['teacher_replay']['mean_locked']:.1f} mean pieces",
    'engine_sha256': fingerprint(ENGINE)}
  summary = {key: value for key, value in data.items() if key != 'scenarios'}
  summary['scenarios'] = []
  for seed in seeds:
    policies, sequences = [], []
    for key, name, model in models:
      if fingerprint(ENGINE) != data['engine_sha256']:
        raise ValueError('Engine changed during demo generation')
      policy, sequence, colors = record_policy(model, seed, output / f'{seed}-{key}.jsonl')
      # The visual demo must reproduce the exact evaluated episode, not merely use its seed.
      evaluated = next(run for run in report['results'][key]['runs'] if run['seed'] == seed)
      for field in ('score', 'locked', 'cleared', 'terminated', 'capped', 'gold_cleared'):
        if policy['result'][field] != evaluated[field]:
          raise ValueError(f'Demo diverges from benchmark: {key}/{seed}/{field}')
      policies.append({'key': key, 'name': name, 'model_sha256': fingerprint(model) if model else None, **policy})
      sequences.append(sequence)
      data['colors'] = colors
      print(json.dumps({'seed': seed, 'policy': key, **policy['result']}), flush=True)
    combined = {}
    for sequence in sequences:
      for identity, piece in sequence.items():
        if identity in combined and combined[identity] != piece:
          raise ValueError('Piece sequence differs between policies')
        combined[identity] = piece
    data['scenarios'].append({'seed': seed, 'policies': policies})
    summary['scenarios'].append({'seed': seed, 'policies': [
      {key: value for key, value in policy.items() if key != 'turns'} for policy in policies]})
  if fingerprint(ENGINE) != data['engine_sha256']:
    raise ValueError('Engine changed during demo generation')
  write_viewer(output / 'index.html', data)
  save(output / 'summary.json', summary)
  return summary


def main():
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument('--experiment', type=Path, default=Path('build/ai/dagger/experiment.json'))
  parser.add_argument('--report', type=Path, default=Path('AI/reports/dagger-experiment.json'))
  parser.add_argument('--output', type=Path, default=Path('build/ai/dagger-demo'))
  parser.add_argument('--seeds', type=int, nargs='+')
  args = parser.parse_args()
  seeds = args.seeds or json.loads(args.report.read_text())['seeds'][:3]
  if not seeds or len(set(seeds)) != len(seeds) or any(not 0 <= s <= 2**31 - 1 for s in seeds):
    parser.error('Require unique nonnegative Int32 seeds')
  torch.set_num_threads(2)
  build_demo(args.experiment, args.report, args.output, seeds)


if __name__ == '__main__':
  main()
