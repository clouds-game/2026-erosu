"""九通道整块棋盘编码和小型 CNN actor-critic。"""

import numpy as np
import torch
from torch import nn
from torch.distributions import Categorical

SHAPES = ['i', 'o', 't', 'l', 'j', 's', 'z']
META_SIZE = 4 * 14 + 14 + 3
OBS_VERSION = 1


def encode(state, objective, remaining_fraction):
  board = np.zeros((9, 18, 10), dtype=np.float32)
  for piece in state['pieces']:
    cells = {(c['x'], c['y']) for c in piece['cells']}
    for x, y in cells:
      board[piece['color'], y, x] = 1
      board[7, y, x] = (x + 1, y) in cells
      board[8, y, x] = (x, y + 1) in cells
  meta = np.zeros(META_SIZE, dtype=np.float32)
  for index, piece in enumerate([state['active'], *state['next'][:3]]):
    if piece:
      meta[index * 14 + SHAPES.index(piece['shape'])] = 1
      meta[index * 14 + 7 + piece['color']] = 1
  weights = state['color_weights']
  meta[56:63] = np.array(weights) / sum(weights)
  meta[63:70] = [color['clear_bonus'] / 3000 for color in state['colors']]
  meta[70:72] = [objective == 'survival', objective == 'score']
  meta[72] = remaining_fraction
  return board, meta


class ActorCritic(nn.Module):
  def __init__(self):
    super().__init__()
    self.board = nn.Sequential(nn.Conv2d(9, 16, 3, padding=1), nn.ReLU(),
      nn.Conv2d(16, 32, 3, padding=1), nn.ReLU(),
      nn.Conv2d(32, 32, 3, padding=1), nn.ReLU(), nn.Flatten(),
      nn.Linear(32 * 18 * 10, 128), nn.ReLU())
    self.metadata = nn.Sequential(nn.Linear(META_SIZE, 64), nn.ReLU())
    self.shared = nn.Sequential(nn.Linear(192, 128), nn.ReLU())
    self.policy = nn.Linear(128, 40)
    self.value = nn.Linear(128, 1)

  def forward(self, board, metadata, mask):
    features = self.shared(torch.cat([self.board(board), self.metadata(metadata)], dim=-1))
    logits = self.policy(features).masked_fill(~mask, -1e9)
    return Categorical(logits=logits), self.value(features).squeeze(-1)
