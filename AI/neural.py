"""九通道整块棋盘编码和小型 CNN actor-critic。"""

import numpy as np
import torch
from torch import nn
from torch.distributions import Categorical

SHAPES = ['i', 'o', 't', 'l', 'j', 's', 'z']
META_SIZE = 4 * 14 + 14 + 3
OBS_VERSION = 1
FEATURE_OBS_VERSION = 2
FEATURE_META_SIZE = META_SIZE + 2 + 7 * 16 + 2


def observation_size(version):
  if version not in (OBS_VERSION, FEATURE_OBS_VERSION):
    raise ValueError("Incompatible observation version")
  return (9, META_SIZE) if version == OBS_VERSION else (11, FEATURE_META_SIZE)


def encode(state, objective, remaining_fraction, observation_version=OBS_VERSION):
  channels, meta_size = observation_size(observation_version)
  board = np.zeros((channels, 18, 10), dtype=np.float32)
  for piece in state['pieces']:
    cells = {(c['x'], c['y']) for c in piece['cells']}
    for x, y in cells:
      board[piece['color'], y, x] = 1
      board[7, y, x] = (x + 1, y) in cells
      board[8, y, x] = (x, y + 1) in cells
      if observation_version == FEATURE_OBS_VERSION:
        board[9, y, x] = piece.get("anchored", False)
  meta = np.zeros(meta_size, dtype=np.float32)
  for index, piece in enumerate([state['active'], *state['next'][:3]]):
    if piece:
      meta[index * 14 + SHAPES.index(piece['shape'])] = 1
      meta[index * 14 + 7 + piece['color']] = 1
  weights = state['color_weights']
  meta[56:63] = np.array(weights) / sum(weights)
  meta[63:70] = [color['clear_bonus'] / 3000 for color in state['colors']]
  meta[70:72] = [objective == 'survival', objective == 'score']
  meta[72] = remaining_fraction
  if observation_version == FEATURE_OBS_VERSION:
    if 'known_enclosed_cells' not in state:
      raise ValueError('Feature-aware observations require enclosure history from the current engine')
    for cell in state['known_enclosed_cells']:
      board[10, cell['y'], cell['x']] = 1
    meta[73:75] = [state['anchored_blocks'], state['enclosed_fill']]
    forecast = state['forecast']
    if len(forecast) > 7:
      raise ValueError('Forecast exceeds the feature observation capacity')
    for index, piece in enumerate(forecast):
      start = 75 + index * 16
      meta[start + (SHAPES + ['single']).index(piece['shape'])] = 1
      meta[start + 8 + piece['color']] = 1
      meta[start + 15] = piece.get('anchored', False)
    meta[-2:] = [state['anchored_spawn_pending'], state['enclosed_fill_pending']]
  return board, meta


class ActorCritic(nn.Module):
  def __init__(self, observation_version=OBS_VERSION):
    super().__init__()
    self.observation_version = observation_version
    channels, meta_size = observation_size(observation_version)
    self.board = nn.Sequential(nn.Conv2d(channels, 16, 3, padding=1), nn.ReLU(),
      nn.Conv2d(16, 32, 3, padding=1), nn.ReLU(),
      nn.Conv2d(32, 32, 3, padding=1), nn.ReLU(), nn.Flatten(),
      nn.Linear(32 * 18 * 10, 128), nn.ReLU())
    self.metadata = nn.Sequential(nn.Linear(meta_size, 64), nn.ReLU())
    self.shared = nn.Sequential(nn.Linear(192, 128), nn.ReLU())
    self.policy = nn.Linear(128, 40)
    self.value = nn.Linear(128, 1)

  def forward(self, board, metadata, mask):
    features = self.shared(torch.cat([self.board(board), self.metadata(metadata)], dim=-1))
    logits = self.policy(features).masked_fill(~mask, -1e9)
    return Categorical(logits=logits), self.value(features).squeeze(-1)


def upgrade(model):
  """扩展输入并零初始化新增权重，保留旧策略输出。"""
  if model.observation_version == FEATURE_OBS_VERSION:
    return model
  expanded = ActorCritic(FEATURE_OBS_VERSION)
  weights = expanded.state_dict()
  for key, value in model.state_dict().items():
    if key == 'board.0.weight':
      weights[key].zero_()
      weights[key][:, :9] = value
    elif key == 'metadata.0.weight':
      weights[key].zero_()
      weights[key][:, :META_SIZE] = value
    else:
      weights[key] = value
  expanded.load_state_dict(weights)
  return expanded
