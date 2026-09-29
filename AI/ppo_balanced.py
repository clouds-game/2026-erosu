"""每种模式固定一个环境、等步数采样的 PPO；兼容现有策略评估与回放。"""

import argparse
from contextlib import ExitStack
import hashlib
import json
import math
from pathlib import Path
import random
import statistics

import torch

from .engine import ENGINE
from .imitation import teacher_targets
from .neural import FEATURE_OBS_VERSION
from .ppo import checkpoint_modes, load_model, tensors
from .ppo_optimization import advantages, update
from .rl_env import PlacementEnv
from .train import save, source_hash


def train(args):
  model, parent = load_model(args.init_model)
  modes = checkpoint_modes(parent)
  if parent['observation_version'] != FEATURE_OBS_VERSION:
    raise ValueError('Balanced training requires feature-aware observation version 2')
  if len({mode.key for mode in modes}) != len(modes):
    raise ValueError('Duplicate game modes would bias training coverage')
  count = len(modes)
  teacher_coef = getattr(args, 'teacher_coef', 0.0)
  if args.steps < count or args.steps % count or min(args.rollout, args.minibatch, args.epochs) < 1:
    raise ValueError('Steps must be a positive multiple of mode count; other budgets must be positive')
  if any(not math.isfinite(value) or value <= 0 for value in (args.learning_rate, args.target_kl)):
    raise ValueError('Learning rate and target KL must be positive')
  if not math.isfinite(teacher_coef) or teacher_coef < 0:
    raise ValueError('Teacher coefficient must be finite and nonnegative')
  outputs = [args.model, args.model.with_suffix('.json'), args.model.with_suffix('.tmp')]
  if len({path.resolve() for path in outputs}) != len(outputs):
    raise ValueError('Checkpoint, report and temporary output paths must differ')
  if args.init_model.resolve() in {path.resolve() for path in outputs}:
    raise ValueError('Initialization and output paths must differ')
  if any(path.exists() for path in outputs):
    raise ValueError('Output already exists; choose a new experiment path')
  engine_hash = hashlib.sha256(ENGINE.read_bytes()).hexdigest()
  if parent['engine_sha256'] != engine_hash:
    raise ValueError('Initialization engine differs; explicitly migrate the checkpoint first')
  torch.manual_seed(args.seed)
  rng = random.Random(args.seed)
  forbidden = set(parent.get('validation_seeds', []))
  training_seeds = set(parent['training_seeds'])
  optimizer = torch.optim.Adam(model.parameters(), lr=args.learning_rate)
  metadata = {**parent, 'algorithm': 'guided_balanced_ppo' if teacher_coef else 'balanced_ppo', 'steps': 0,
    'critic_status': 'ppo_fitted', 'selection': 'final_budget',
    'initialization_sha256': hashlib.sha256(args.init_model.read_bytes()).hexdigest(),
    'source_sha256': source_hash(), 'training_seed': args.seed,
    'torch_version': str(torch.__version__), 'training_seeds': [],
    'optimizer': {'learning_rate': args.learning_rate, 'final_lr_fraction': 0.1,
      'target_kl': args.target_kl, 'value_loss': 'huber', 'epochs': args.epochs,
      'teacher_coef': teacher_coef,
      'minibatch': args.minibatch, 'rollout_per_mode': args.rollout,
      'gamma': 0.99, 'gae_lambda': 0.95}}
  episodes, history = [], []
  mode_steps = {mode.key: 0 for mode in modes}
  mode_episodes = dict.fromkeys(mode_steps, 0)
  args.model.parent.mkdir(parents=True, exist_ok=True)

  def reset(env, mode):
    seed = rng.randrange(7_000_000, 8_000_000)
    while seed in forbidden or seed in training_seeds:
      seed = rng.randrange(7_000_000, 8_000_000)
    training_seeds.add(seed)
    return env.reset(seed, mode)

  steps = 0
  with ExitStack() as stack:
    envs = [stack.enter_context(PlacementEnv(parent['objective'], parent['max_pieces'],
      mode.color_profile, observation_version=FEATURE_OBS_VERSION)) for mode in modes]
    observations = [reset(env, mode) for env, mode in zip(envs, modes)]
    while steps < args.steps:
      rollout_length = min(args.rollout, (args.steps - steps) // count)
      collected, actions, logs, values, rewards, dones = [], [], [], [], [], []
      demonstrations = []
      for _ in range(rollout_length):
        with torch.no_grad():
          distribution, value = model(*tensors(observations))
          action = distribution.sample()
        collected.extend(observations)
        actions.append(action)
        logs.append(distribution.log_prob(action).detach())
        values.append(value)
        if teacher_coef:
          demonstrations.extend(torch.from_numpy(teacher_targets(env.state, env.actions)) for env in envs)
        step_rewards, step_dones = [], []
        for index, (env, mode) in enumerate(zip(envs, modes)):
          observation, reward, done, info = env.step(action[index].item())
          mode_steps[mode.key] += 1
          step_rewards.append(reward)
          step_dones.append(done)
          if done:
            episodes.append(info)
            mode_episodes[mode.key] += 1
            observation = reset(env, mode)
          observations[index] = observation
        rewards.append(step_rewards)
        dones.append(step_dones)
        steps += count
      with torch.no_grad():
        _, bootstrap = model(*tensors(observations))
      advantage, returns = advantages(rewards, torch.stack(values), dones, bootstrap)
      batch = (*tensors(collected), torch.cat(actions), torch.cat(logs),
        returns.flatten(), advantage.flatten())
      # Decay over the requested budget, retaining a small learning rate at its end.
      fraction = (steps - count * rollout_length) / max(1, args.steps - count * rollout_length)
      learning_rate = args.learning_rate * (1 - 0.9 * fraction)
      for group in optimizer.param_groups:
        group['lr'] = learning_rate
      metrics = update(model, optimizer, batch, epochs=args.epochs,
        minibatch=args.minibatch, target_kl=args.target_kl,
        teacher_targets=torch.stack(demonstrations) if demonstrations else None, teacher_coef=teacher_coef)
      recent = episodes[-60:]
      row = {'steps': steps, 'episodes': len(episodes), 'learning_rate': learning_rate,
        'recent_mean_locked': statistics.mean(run['locked'] for run in recent) if recent else None,
        'mode_steps': dict(mode_steps), 'mode_episodes': dict(mode_episodes), **metrics}
      history.append(row)
      print(json.dumps(row), flush=True)
      current = {**metadata, 'steps': steps, 'mode_steps': dict(mode_steps),
        'on_policy_teacher_labels': steps if teacher_coef else 0,
        'training_seeds': sorted(training_seeds)}
      temporary = args.model.with_suffix('.tmp')
      torch.save({'metadata': current, 'weights': model.state_dict()}, temporary)
      temporary.replace(args.model)
      save(args.model.with_suffix('.json'), {**current, 'history': history, 'episodes': episodes})
  return model


def main():
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument('--init-model', type=Path, required=True)
  parser.add_argument('--model', type=Path, required=True)
  parser.add_argument('--steps', type=int, default=61440)
  parser.add_argument('--rollout', type=int, default=64, help='Steps per mode in one rollout')
  parser.add_argument('--epochs', type=int, default=4)
  parser.add_argument('--minibatch', type=int, default=128)
  parser.add_argument('--learning-rate', type=float, default=3e-4)
  parser.add_argument('--target-kl', type=float, default=0.02)
  parser.add_argument('--teacher-coef', type=float, default=0.0,
    help='Optional teacher cross-entropy weight on visited states; zero is pure PPO')
  parser.add_argument('--seed', type=int, default=46)
  parser.add_argument('--threads', type=int, default=1)
  args = parser.parse_args()
  if args.threads < 1 or not 0 <= args.seed < 2**31:
    parser.error('Require positive threads and nonnegative Int32 seed')
  torch.set_num_threads(args.threads)
  train(args)


if __name__ == '__main__':
  main()
