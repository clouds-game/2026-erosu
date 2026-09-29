"""可复现的十二种自由模式组合；每局内规则保持不变。"""
from dataclasses import asdict, dataclass


@dataclass(frozen=True)
class GameMode:
  color_profile: str = 'rare_seven'
  anchored_blocks: bool = False
  enclosed_fill: bool = False

  def __post_init__(self):
    if self.color_profile not in ('classic', 'rare_six', 'rare_seven'):
      raise ValueError('Unknown color profile')

  @property
  def key(self):
    return self.color_profile + ('_anchored' if self.anchored_blocks else '') + ('_fill' if self.enclosed_fill else '')

  def options(self):
    return asdict(self)


ALL_MODES = tuple(GameMode(profile, anchored, fill)
  for profile in ('classic', 'rare_six', 'rare_seven')
  for anchored in (False, True) for fill in (False, True))


def find_mode(key):
  return next(mode for mode in ALL_MODES if mode.key == key)
