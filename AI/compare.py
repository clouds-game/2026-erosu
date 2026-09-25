"""同一种子下录制、验证并导出训练前后双盘回放。"""

import argparse
import hashlib
import json
from pathlib import Path

from .engine import Engine, ROOT
from .policy import BASELINE
from .train import episode, load, save
from .trajectory import read, replay


def frames(steps):
  turns = [[]]
  for step in steps:
    state = step['response']['state']
    if state is None:
      continue
    turns[-1].append({key: state[key] for key in
      ['pieces', 'active', 'wave', 'phase', 'score', 'locked', 'cleared', 'best_chain']})
    if state['phase'] in ('falling', 'over', 'won') and state['locked'] == len(turns):
      turns.append([])
  if not turns[-1]:
    turns.pop()
  return turns


def compare(model, seed, max_pieces, output):
  weights = load(model)
  output.mkdir(parents=True, exist_ok=True)
  data = {'seed': seed, 'max_pieces': max_pieces,
    'model_sha256': hashlib.sha256(model.read_bytes()).hexdigest(), 'policies': []}
  for name, policy in [('Before training', BASELINE), ('After training', weights)]:
    filename = 'before.jsonl' if policy is BASELINE else 'after.jsonl'
    path = output / filename
    with path.open('w', encoding='utf-8') as trace, Engine(trace) as engine:
      result = episode(engine, seed, policy, max_pieces)
    verified = replay(path)
    _, steps = read(path)
    data['policies'].append({'name': name, 'result': result, 'verified_steps': verified['steps'],
      'turns': frames(steps)})
  # Identical seed should expose the same piece identities/shapes/colors, independent of decisions.
  sequences = []
  for filename in ['before.jsonl', 'after.jsonl']:
    _, steps = read(output / filename)
    sequence = {}
    for step in steps:
      active = step['response']['state']['active']
      if active:
        sequence[active['id']] = (active['shape'], active['color'])
    sequences.append(sequence)
  common = sequences[0].keys() & sequences[1].keys()
  if any(sequences[0][key] != sequences[1][key] for key in common):
    raise ValueError('Piece sequences differ; comparison is not fair')
  summary = {key: value for key, value in data.items() if key != 'policies'}
  summary['policies'] = [{key: value for key, value in policy.items() if key != 'turns'} for policy in data['policies']]
  save(output / 'summary.json', summary)
  template = (ROOT / 'AI/replay_compare.html').read_text(encoding='utf-8')
  (output / 'index.html').write_text(template.replace('__REPLAY_DATA__', json.dumps(data).replace('<', '\\u003c')), encoding='utf-8')
  return summary


def main():
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument('--model', type=Path, default=ROOT / 'AI/models/starter.json')
  parser.add_argument('--seed', type=int, default=3000100)
  parser.add_argument('--max-pieces', type=int, default=150)
  parser.add_argument('--output', type=Path, default=ROOT / 'build/ai/comparison')
  args = parser.parse_args()
  if not 0 <= args.seed <= 2**31 - 1 or args.max_pieces < 1:
    parser.error('Require an Int32 nonnegative seed and positive max-pieces')
  print(json.dumps(compare(args.model, args.seed, args.max_pieces, args.output), indent=2))


if __name__ == '__main__':
  main()
