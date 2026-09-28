"""多环境 GAE、限制策略偏移的 PPO 更新，以及训练诊断。"""

import math

import torch
from torch import nn
from torch.nn import functional as F


def advantages(rewards, values, dones, bootstrap, gamma=0.99, lam=0.95):
  """按环境独立计算 [时间, 环境] 的优势；done 同时切断引导值和递推。"""
  values = torch.as_tensor(values)
  if not values.is_floating_point():
    values = values.float()
  rewards = torch.as_tensor(rewards, dtype=values.dtype, device=values.device)
  dones = torch.as_tensor(dones, device=values.device)
  bootstrap = torch.as_tensor(bootstrap, dtype=values.dtype, device=values.device)
  if values.ndim != 2 or 0 in values.shape:
    raise ValueError('Values must have nonempty [time, env] dimensions')
  if rewards.shape != values.shape or dones.shape != values.shape:
    raise ValueError('Rewards, values and dones must have matching [time, env] dimensions')
  if bootstrap.shape != values.shape[1:]:
    raise ValueError('Bootstrap must have one value per environment')
  if not 0 <= gamma <= 1 or not 0 <= lam <= 1:
    raise ValueError('Gamma and lambda must be between zero and one')
  if not all(torch.isfinite(item).all() for item in (rewards, values, bootstrap)):
    raise ValueError('Rewards, values and bootstrap must be finite')
  if not ((dones == 0) | (dones == 1)).all():
    raise ValueError('Dones must contain only boolean values')
  with torch.no_grad():
    result = torch.zeros_like(values)
    carry = torch.zeros_like(bootstrap)
    for index in reversed(range(len(rewards))):
      following = bootstrap if index == len(rewards) - 1 else values[index + 1]
      alive = 1 - dones[index].to(values.dtype)
      delta = rewards[index] + gamma * following * alive - values[index]
      carry = delta + gamma * lam * alive * carry
      result[index] = carry
    return result, result + values


def update(model, optimizer, batch, epochs=4, minibatch=128, target_kl=0.02,
    huber_value=True, clip_range=0.2, value_coef=0.5, entropy_coef=0.01,
    max_grad_norm=0.5, teacher_targets=None, teacher_coef=0.0):
  """更新展平后的 rollout；返回实际更新次数及有限数值诊断。"""
  board, meta, mask, actions, old_logs, returns, advantage = batch
  count = len(actions)
  if count == 0 or epochs < 1 or minibatch < 1:
    raise ValueError('Require a nonempty batch and positive epoch/minibatch counts')
  if any(len(item) != count for item in batch):
    raise ValueError('All batch tensors must have equal leading dimensions')
  if any(item.shape != (count,) for item in (actions, old_logs, returns, advantage)):
    raise ValueError('Actions, log probabilities, returns and advantages must be vectors')
  if mask.ndim != 2 or mask.dtype != torch.bool or not mask.any(-1).all():
    raise ValueError('Each observation requires a boolean mask with a legal action')
  if actions.dtype != torch.long or (actions < 0).any() or (actions >= mask.shape[1]).any():
    raise ValueError('Actions must be valid integer indices')
  if not mask.gather(1, actions[:, None]).all():
    raise ValueError('Rollout contains a masked action')
  if not all(torch.isfinite(item).all() for item in (old_logs, returns, advantage)):
    raise ValueError('Rollout targets must be finite')
  if target_kl is not None and (not math.isfinite(target_kl) or target_kl <= 0):
    raise ValueError('Target KL must be positive or None')
  if not 0 < clip_range < 1 or max_grad_norm <= 0:
    raise ValueError('Require a clip range between zero and one and positive gradient norm')
  if not math.isfinite(teacher_coef) or teacher_coef < 0:
    raise ValueError('Teacher coefficient must be finite and nonnegative')
  if teacher_coef > 0 and teacher_targets is None:
    raise ValueError('Teacher guidance requires targets for every rollout observation')
  if teacher_targets is not None:
    teacher_targets = torch.as_tensor(teacher_targets, dtype=old_logs.dtype,
      device=old_logs.device).detach()
    if teacher_targets.shape != mask.shape:
      raise ValueError('Teacher targets must have matching [observation, action] dimensions')
    if not torch.isfinite(teacher_targets).all() or (teacher_targets < 0).any():
      raise ValueError('Teacher targets must be finite and nonnegative')
    if (teacher_targets[~mask] != 0).any():
      raise ValueError('Teacher targets may assign probability only to legal actions')
    if not torch.allclose(teacher_targets.sum(-1), torch.ones_like(old_logs), atol=1e-5, rtol=1e-5):
      raise ValueError('Teacher targets must sum to one for each observation')

  old_logs, returns, advantage = (item.detach() for item in (old_logs, returns, advantage))
  advantage = (advantage - advantage.mean()) / (advantage.std(unbiased=False) + 1e-8)
  totals = {key: 0.0 for key in ('loss', 'policy_loss', 'value_loss', 'entropy', 'teacher_loss')}
  visited = optimizer_steps = epochs_started = 0
  gradient_total = max_kl = 0.0
  early_stopped = False
  model.train()
  for epoch in range(epochs):
    epochs_started = epoch + 1
    for indices in torch.randperm(count, device=actions.device).split(minibatch):
      distribution, value = model(board[indices], meta[indices], mask[indices])
      log_ratio = distribution.log_prob(actions[indices]) - old_logs[indices]
      ratio = log_ratio.exp()
      # This sampled KL estimate is nonnegative and zero when the policy is unchanged.
      kl = ((ratio - 1) - log_ratio).mean().detach().item()
      if not math.isfinite(kl):
        raise RuntimeError('Non-finite PPO policy divergence')
      max_kl = max(max_kl, kl)
      if target_kl is not None and kl > target_kl:
        early_stopped = True
        break
      policy_loss = -torch.minimum(ratio * advantage[indices],
        ratio.clamp(1 - clip_range, 1 + clip_range) * advantage[indices]).mean()
      # Huber bounds the critic's residual gradient when a rare long game changes its target.
      value_loss = (F.smooth_l1_loss(value, returns[indices], beta=1.0) if huber_value
        else F.mse_loss(value, returns[indices]))
      entropy = distribution.entropy().mean()
      teacher_loss = (-(teacher_targets[indices] * distribution.logits).sum(-1).mean()
        if teacher_targets is not None else policy_loss.new_zeros(()))
      loss = policy_loss + value_coef * value_loss - entropy_coef * entropy + teacher_coef * teacher_loss
      if not torch.isfinite(loss):
        raise RuntimeError('Non-finite PPO loss')
      optimizer.zero_grad()
      loss.backward()
      grad_norm = nn.utils.clip_grad_norm_(model.parameters(), max_grad_norm)
      if not torch.isfinite(grad_norm):
        optimizer.zero_grad()
        raise RuntimeError('Non-finite PPO gradient')
      optimizer.step()
      gradient_total += grad_norm.item()
      optimizer_steps += 1
      size = len(indices)
      visited += size
      for key, item in (('loss', loss), ('policy_loss', policy_loss),
          ('value_loss', value_loss), ('entropy', entropy), ('teacher_loss', teacher_loss)):
        totals[key] += item.detach().item() * size
    if early_stopped:
      break

  # Report divergence and value fit for the final policy on the complete rollout.
  with torch.no_grad():
    distribution, values = model(board, meta, mask)
    log_ratio = distribution.log_prob(actions) - old_logs
    ratio = log_ratio.exp()
    kl = ((ratio - 1) - log_ratio).mean().item()
    clip_fraction = ((ratio - 1).abs() > clip_range).float().mean().item()
    variance = returns.var(unbiased=False).item()
    explained_variance = 1 - (returns - values).var(unbiased=False).item() / variance if variance > 1e-8 else 0.0
    if not visited:
      totals['entropy'] = distribution.entropy().mean().item()
  diagnostics = {key: value / max(1, visited) for key, value in totals.items()}
  diagnostics.update(kl=kl, max_minibatch_kl=max_kl, clip_fraction=clip_fraction,
    explained_variance=explained_variance, grad_norm=gradient_total / max(1, optimizer_steps),
    epochs=epochs_started, optimizer_steps=optimizer_steps, early_stopped=early_stopped)
  if not all(math.isfinite(value) for value in diagnostics.values()):
    raise RuntimeError('Non-finite PPO diagnostics')
  return diagnostics
