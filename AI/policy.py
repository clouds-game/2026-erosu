"""有限落点估计器；不会模拟消除、重力连锁或计分。"""

FEATURES = ["height", "holes", "roughness", "landing_height", "same_color_neighbors", "other_neighbors"]
BASELINE = [-1.0, -2.0, -0.5, -0.5, 2.0, -0.2]


def candidates(state):
  occupied = {(cell["x"], cell["y"]): piece for piece in state["pieces"] for cell in piece["cells"]}
  cells = [(cell["x"], cell["y"]) for cell in state["active"]["cells"]]
  min_x, min_y = min(x for x, y in cells), min(y for x, y in cells)
  cells = [(x - min_x, y - min_y) for x, y in cells]
  seen = set()
  for rotation in range(4):
    orientation = tuple(sorted(cells))
    if orientation not in seen:
      seen.add(orientation)
      for left in range(state["width"] - max(x for x, y in cells)):
        def valid(top):
          return all(y + top < state["height"] and (x + left, y + top) not in occupied for x, y in cells)
        if not valid(min_y):
          continue
        top = min_y
        while valid(top + 1):
          top += 1
        landing = {(x + left, y + top) for x, y in cells}
        filled = set(occupied) | landing
        heights = [state["height"] - min((y for x, y in filled if x == column), default=state["height"])
          for column in range(state["width"])]
        holes = sum((x, y) not in filled for x, height in enumerate(heights)
          for y in range(state["height"] - height, state["height"]))
        neighbors = {}
        for x, y in landing:
          for cell in [(x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)]:
            if cell in occupied:
              piece = occupied[cell]
              neighbors[piece["id"]] = piece["color"]
        same = sum(color == state["active"]["color"] for color in neighbors.values())
        features = [sum(heights) / state["width"], holes / state["width"],
          sum(abs(a - b) for a, b in zip(heights, heights[1:])) / state["width"],
          state["height"] - top, same, len(neighbors) - same]
        yield rotation, left, features
    height = max(y for x, y in cells) + 1
    cells = [(height - 1 - y, x) for x, y in cells]


def choose(state, weights, random):
  options = list(candidates(state))
  if not options:
    return 0, min(cell["x"] for cell in state["active"]["cells"])
  if weights is None:
    rotation, left, _ = random.choice(options)
  else:
    rotation, left, _ = max(options, key=lambda option: sum(w * f for w, f in zip(weights, option[2])))
  return rotation, left
