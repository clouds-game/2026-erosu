"""每步执行一次合法整块放置；仅使用外部 tick 完成结算。"""

import numpy as np
from .engine import Engine
from .neural import encode, OBS_VERSION
from .modes import GameMode


class PlacementEnv:
  def __init__(self, objective, max_pieces=300, profile='rare_seven', trace=None, observation_version=OBS_VERSION):
    if objective not in ('survival', 'score') or max_pieces < 1:
      raise ValueError('Require survival/score objective and a positive piece budget')
    self.mode = GameMode(profile)
    self.observation_version = observation_version
    self.engine = Engine(trace)
    self.objective, self.max_pieces, self.profile = objective, max_pieces, profile

  def reset(self, seed, mode=None):
    self.seed = seed
    self.mode = mode or GameMode(self.profile)
    if self.observation_version == OBS_VERSION and (self.mode.anchored_blocks or self.mode.enclosed_fill):
      raise ValueError('Optional features require observation version 2')
    self.state = self.engine.command('start', mode='endless', seed=seed, **self.mode.options())['state']
    return self.observe()

  def observe(self):
    actions = self.engine.command('placements')['actions']
    self.actions = actions
    mask = np.array([a['piece'] is not None for a in actions], dtype=bool)
    if not mask.any():
      raise RuntimeError('No legal action at a live decision point')
    board, meta = encode(self.state, self.objective, max(0, 1 - self.state['locked'] / self.max_pieces), self.observation_version)
    return board, meta, mask

  def step(self, action):
    previous = self.state['score']
    response = self.engine.command('place', action=int(action))
    if not response['applied']:
      raise RuntimeError('Masked placement rejected by engine')
    self.state = response['state']
    for _ in range(3600):
      if self.state['phase'] not in ('clearing', 'settling'):
        break
      self.state = self.engine.command('tick', count=1)['state']
    else:
      raise RuntimeError('Resolution exceeded tick budget')
    reward = 1.0 if self.objective == 'survival' else (self.state['score'] - previous) / 1000
    terminated = self.state['is_finished']
    capped = self.state['locked'] >= self.max_pieces and not terminated
    done = terminated or capped
    # The finite piece budget is part of the task and observation, so no bootstrap at its end.
    return None if done else self.observe(), reward, done, {
      'mode': self.mode.key, **self.mode.options(), 'seed': self.seed, 'score': self.state['score'], 'locked': self.state['locked'],
      'cleared': self.state['cleared'], 'terminated': terminated, 'capped': capped}

  def close(self):
    self.engine.close()

  def __enter__(self):
    return self

  def __exit__(self, *args):
    self.engine.__exit__(*args)
