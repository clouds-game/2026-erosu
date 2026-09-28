"""有限步数任务的 PPO：CNN 观察、合法动作掩码、独立生存/得分目标。"""

import argparse
import hashlib
import json
from pathlib import Path
import random
import statistics

import numpy as np
import torch
from torch import nn

from .engine import ENGINE
from .neural import ActorCritic, OBS_VERSION, FEATURE_OBS_VERSION, upgrade
from .modes import ALL_MODES, GameMode, find_mode
from .rl_env import PlacementEnv
from .train import source_hash, save


def tensors(observations):
  return tuple(torch.from_numpy(np.stack(items)) for items in zip(*observations))


def advantages(rewards, values, dones, bootstrap, gamma=0.99, lam=0.95):
  result = torch.zeros(len(rewards))
  carry = 0.0
  for index in reversed(range(len(rewards))):
    following = bootstrap if index == len(rewards) - 1 else values[index + 1]
    alive = 1.0 - float(dones[index])
    delta = rewards[index] + gamma * following * alive - values[index]
    carry = delta + gamma * lam * alive * carry
    result[index] = carry
  return result, result + torch.tensor(values)


def update(model, optimizer, batch, epochs=4, minibatch=64):
  board, meta, mask, actions, old_logs, returns, advantage = batch
  advantage = (advantage - advantage.mean()) / (advantage.std(unbiased=False) + 1e-8)
  losses = []
  for _ in range(epochs):
    for indices in torch.randperm(len(actions)).split(minibatch):
      distribution, value = model(board[indices], meta[indices], mask[indices])
      ratio = (distribution.log_prob(actions[indices]) - old_logs[indices]).exp()
      policy_loss = -torch.min(ratio * advantage[indices],
        ratio.clamp(0.8, 1.2) * advantage[indices]).mean()
      value_loss = (value - returns[indices]).square().mean()
      loss = policy_loss + 0.5 * value_loss - 0.01 * distribution.entropy().mean()
      if not torch.isfinite(loss):
        raise RuntimeError('Non-finite PPO loss')
      optimizer.zero_grad()
      loss.backward()
      nn.utils.clip_grad_norm_(model.parameters(), 0.5)
      optimizer.step()
      losses.append(loss.item())
  return statistics.mean(losses)


def train(args):
  random.seed(args.seed)
  np.random.seed(args.seed)
  torch.manual_seed(args.seed)
  mixed_features = getattr(args, "mixed_features", False)
  requested_mode = getattr(args, "game_mode", None)
  modes = list(ALL_MODES) if mixed_features else [find_mode(requested_mode) if requested_mode else GameMode(args.color_profile)]
  version = FEATURE_OBS_VERSION if mixed_features or requested_mode else OBS_VERSION
  model = ActorCritic(version)
  parent = None
  if args.init_model is not None:
    model, parent = load_model(args.init_model)
    for key in ('objective', 'max_pieces'):
      if parent[key] != getattr(args, key):
        raise ValueError(f'Initialization task mismatch: {key}')
    if version == FEATURE_OBS_VERSION:
      model = upgrade(model)
    elif parent['observation_version'] != version or parent['color_profile'] != args.color_profile:
      raise ValueError('Initialization observation/profile mismatch')
    output_paths = (args.model, args.model.with_suffix('.initial.pt'), args.model.with_suffix('.json'), args.model.with_suffix('.tmp'))
    if args.init_model.resolve() in {path.resolve() for path in output_paths}:
      raise ValueError('Initialization and output checkpoints must differ')
  model.train()
  torch.manual_seed(args.seed)
  optimizer = torch.optim.Adam(model.parameters(), lr=3e-4)
  rng = random.Random(args.seed)
  mode_rng = random.Random(args.seed + 1)
  mode_cycle = []
  training_seeds, episodes, history = [], [], []
  initial_metadata = {'observation_version': version, 'objective': args.objective,
    'color_conditioned': model.color_conditioned,
    'color_profile': 'mixed' if mixed_features else modes[0].color_profile,
    'game_modes': [mode.options() for mode in modes], 'max_pieces': args.max_pieces,
    'training_seed': args.seed, 'training_seeds': list(parent['training_seeds']) if parent else [],
    'validation_seeds': list(parent.get('validation_seeds', [])) if parent else [],
    'initialization_sha256': hashlib.sha256(args.init_model.read_bytes()).hexdigest() if parent else None,
    'steps': 0,
    'source_sha256': source_hash(), 'engine_sha256': hashlib.sha256(ENGINE.read_bytes()).hexdigest(),
    'torch_version': str(torch.__version__), 'parameters': sum(p.numel() for p in model.parameters())}
  args.model.parent.mkdir(parents=True, exist_ok=True)
  torch.save({'metadata': initial_metadata, 'weights': model.state_dict()}, args.model.with_suffix('.initial.pt'))
  env = PlacementEnv(args.objective, args.max_pieces, modes[0].color_profile, observation_version=version)

  def reset():
    seed = rng.randrange(1_000_000)
    training_seeds.append(seed)
    if not mode_cycle:
      mode_cycle.extend(modes)
      mode_rng.shuffle(mode_cycle)
    return env.reset(seed, mode_cycle.pop())

  observation = reset()
  steps = 0
  try:
    while steps < args.steps:
      observations, actions, logs, values, rewards, dones = [], [], [], [], [], []
      for _ in range(min(args.rollout, args.steps - steps)):
        with torch.no_grad():
          distribution, value = model(*tensors([observation]))
          action = distribution.sample()
        observations.append(observation)
        actions.append(action.item())
        logs.append(distribution.log_prob(action).item())
        values.append(value.item())
        observation, reward, done, info = env.step(action.item())
        rewards.append(reward)
        dones.append(done)
        steps += 1
        if done:
          episodes.append(info)
          observation = reset()
      with torch.no_grad():
        _, value = model(*tensors([observation]))
      advantage, returns = advantages(rewards, values, dones, value.item())
      batch = (*tensors(observations), torch.tensor(actions), torch.tensor(logs), returns, advantage)
      loss = update(model, optimizer, batch)
      recent = episodes[-20:]
      row = {'steps': steps, 'episodes': len(episodes), 'loss': loss,
        'recent_mean_locked': statistics.mean(x['locked'] for x in recent) if recent else None,
        'recent_mean_score': statistics.mean(x['score'] for x in recent) if recent else None,
        'mode_episodes': {mode.key: sum(x['mode'] == mode.key for x in episodes) for mode in modes}}
      history.append(row)
      print(json.dumps(row), flush=True)
      metadata = {**initial_metadata, 'steps': steps,
        'training_seeds': sorted(set(initial_metadata['training_seeds']) | set(training_seeds)),
        'source_sha256': source_hash()}
      args.model.parent.mkdir(parents=True, exist_ok=True)
      temporary = args.model.with_suffix('.tmp')
      torch.save({'metadata': metadata, 'weights': model.state_dict()}, temporary)
      temporary.replace(args.model)
      save(args.model.with_suffix('.json'), {**metadata, 'history': history, 'episodes': episodes})
  finally:
    env.close()


def load_model(path):
  checkpoint = torch.load(path, map_location='cpu', weights_only=True)
  metadata = checkpoint['metadata']
  model = ActorCritic(metadata['observation_version'], color_conditioned=metadata.get('color_conditioned', False))
  model.load_state_dict(checkpoint['weights'])
  model.eval()
  return model, metadata


def checkpoint_modes(metadata, requested=None):
  modes = [GameMode(**mode) for mode in metadata.get('game_modes',
    [GameMode(metadata['color_profile']).options()] if metadata['color_profile'] != 'mixed' else [])]
  if not modes:
    raise ValueError('Checkpoint has no supported game modes')
  if requested is not None:
    modes = [mode for mode in modes if mode.key == requested]
    if not modes:
      raise ValueError('Requested game mode was not part of training')
  return modes


def summarize(runs):
  return {'mean_locked': statistics.mean(x['locked'] for x in runs),
    'median_locked': statistics.median(x['locked'] for x in runs),
    'mean_score': statistics.mean(x['score'] for x in runs),
    'capped': sum(x['capped'] for x in runs)}


def evaluate(args):
  model, metadata = load_model(args.model)
  torch.manual_seed(args.seed)
  seeds = list(range(args.seed, args.seed + args.episodes))
  if set(seeds) & (set(metadata['training_seeds']) | set(metadata.get('validation_seeds', []))):
    raise ValueError('Evaluation seeds overlap training or model-selection seeds')
  modes = checkpoint_modes(metadata, getattr(args, "game_mode", None))
  runs, by_mode = [], {}
  with PlacementEnv(metadata['objective'], metadata['max_pieces'], modes[0].color_profile,
      observation_version=metadata['observation_version']) as env:
    for mode in modes:
      mode_runs = []
      for seed in seeds:
        observation = env.reset(seed, mode)
        while True:
          with torch.no_grad():
            distribution, _ = model(*tensors([observation]))
            action = (distribution.sample() if args.sample_actions else distribution.logits.argmax(-1)).item()
          observation, _, done, info = env.step(action)
          if done:
            mode_runs.append(info)
            break
      by_mode[mode.key] = summarize(mode_runs)
      runs.extend(mode_runs)
      print(json.dumps({'mode': mode.key, **by_mode[mode.key]}), flush=True)
  report = {'model_sha256': hashlib.sha256(args.model.read_bytes()).hexdigest(),
    'action_selection': 'sample' if args.sample_actions else 'greedy',
    'metadata': metadata, 'evaluation_engine_sha256': hashlib.sha256(ENGINE.read_bytes()).hexdigest(),
    'summary': summarize(runs), 'by_mode': by_mode, 'runs': runs}
  save(args.report, report)
  print(json.dumps(report['summary']), flush=True)


def play(args):
  from .trajectory import replay
  model, metadata = load_model(args.model)
  args.trace.parent.mkdir(parents=True, exist_ok=True)
  mode = checkpoint_modes(metadata, getattr(args, "game_mode", None))[0]
  with args.trace.open('x', encoding='utf-8') as trace, PlacementEnv(
      metadata['objective'], metadata['max_pieces'], mode.color_profile, trace,
      observation_version=metadata['observation_version']) as env:
    observation = env.reset(args.seed, mode)
    while True:
      with torch.no_grad():
        distribution, _ = model(*tensors([observation]))
        action = distribution.logits.argmax(-1).item()
      observation, _, done, info = env.step(action)
      if done:
        break
  verified = replay(args.trace)
  print(json.dumps({'result': info, 'verified': verified['verified'], 'replay_steps': verified['steps']}))


def main():
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument('mode', choices=['train', 'evaluate', 'play'])
  parser.add_argument('--model', type=Path, required=True)
  parser.add_argument('--init-model', type=Path, help='Initialize weights from a compatible checkpoint; starts a fresh optimizer')
  parser.add_argument('--objective', choices=['survival', 'score'], default='survival')
  parser.add_argument('--color-profile', choices=['classic', 'rare_six', 'rare_seven'], default='rare_seven')
  parser.add_argument('--mixed-features', action='store_true', help='Train one version-2 policy across all twelve free-play modes')
  parser.add_argument('--game-mode', choices=[mode.key for mode in ALL_MODES], help='Train/play/evaluate a specific feature combination')
  parser.add_argument('--max-pieces', type=int, default=300)
  parser.add_argument('--steps', type=int, default=16384)
  parser.add_argument('--rollout', type=int, default=256)
  parser.add_argument('--seed', type=int, default=42)
  parser.add_argument('--episodes', type=int, default=30)
  parser.add_argument('--sample-actions', action='store_true', help='Sample actions during evaluation; default is greedy')
  parser.add_argument('--threads', type=int, default=2)
  parser.add_argument('--trace', type=Path, default=Path('build/ai/ppo-game.jsonl'))
  parser.add_argument('--report', type=Path, default=Path('build/ai/ppo-evaluation.json'))
  args = parser.parse_args()
  if min(args.max_pieces, args.steps, args.episodes, args.threads) < 1 or args.rollout < 2:
    parser.error('Require positive budgets and rollout >= 2')
  if not 0 <= args.seed <= 2**31 - args.episodes:
    parser.error('Seeds must fit nonnegative Int32')
  if args.mixed_features and args.game_mode:
    parser.error('Choose mixed features or one game mode')
  torch.set_num_threads(args.threads)
  {'train': train, 'evaluate': evaluate, 'play': play}[args.mode](args)


if __name__ == '__main__':
  main()
