"""版本化轨迹记录、确定性验证和终端回放。"""

import argparse
import hashlib
import json
from pathlib import Path
import sys
import time


def encode(value):
  return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True)


class Recorder:
  def __init__(self, stream, engine_path):
    self.stream = stream
    self.count = 0
    self.digest = hashlib.sha256()
    self.write({"type": "header", "format": "chroma_trajectory", "version": 1,
      "protocol_version": 1, "tick_rate": 60,
      "engine_sha256": hashlib.sha256(engine_path.read_bytes()).hexdigest()})

  def write(self, value):
    self.stream.write(encode(value) + "\n")
    self.stream.flush()

  def record(self, request, response):
    value = {"type": "step", "index": self.count, "request": request, "response": response}
    self.digest.update((encode(value) + "\n").encode())
    self.write(value)
    self.count += 1

  def finish(self):
    self.write({"type": "end", "steps": self.count, "sha256": self.digest.hexdigest()})


def read(path):
  # Validate the whole file before executing any action. No silent partial replay.
  with path.open(encoding="utf-8") as source:
    records = [json.loads(line) for line in source]
  if len(records) < 2:
    raise ValueError("Incomplete trajectory: missing header or end marker")
  header, end = records[0], records[-1]
  if not isinstance(header, dict) or header.get("format") != "chroma_trajectory" or header.get("version") != 1:
    raise ValueError("Unsupported trajectory format/version")
  if header.get("type") != "header" or header.get("protocol_version") != 1 or header.get("tick_rate") != 60:
    raise ValueError("Unsupported protocol or tick rate")
  digest = hashlib.sha256()
  steps = records[1:-1]
  for index, step in enumerate(steps):
    if not isinstance(step, dict) or step.get("type") != "step" or step.get("index") != index:
      raise ValueError(f"Invalid step index at {index}")
    if not isinstance(step.get("request"), dict) or not isinstance(step.get("response"), dict):
      raise ValueError(f"Invalid request/response at step {index}")
    digest.update((encode(step) + "\n").encode())
  if not isinstance(end, dict) or end.get("type") != "end" or end.get("steps") != len(steps):
    raise ValueError("Incomplete trajectory or incorrect step count")
  if end.get("sha256") != digest.hexdigest():
    raise ValueError("Trajectory checksum mismatch")
  return header, steps


def difference(expected, actual, path="response"):
  if type(expected) is not type(actual):
    return path
  if isinstance(expected, dict):
    if expected.keys() != actual.keys():
      return path
    for key in expected:
      found = difference(expected[key], actual[key], f"{path}.{key}")
      if found:
        return found
  elif isinstance(expected, list):
    if len(expected) != len(actual):
      return path
    for index, (left, right) in enumerate(zip(expected, actual)):
      found = difference(left, right, f"{path}[{index}]")
      if found:
        return found
  elif expected != actual:
    return path
  return None


def board(state):
  if state is None:
    return "No game started"
  grid = [["." for _ in range(state["width"])] for _ in range(state["height"])]
  for piece in state["pieces"]:
    for cell in piece["cells"]:
      grid[cell["y"]][cell["x"]] = str(piece["color"])
  if state["active"]:
    for cell in state["active"]["cells"]:
      grid[cell["y"]][cell["x"]] = chr(ord("A") + state["active"]["color"])
  return "\n".join(" ".join(row) for row in grid) + f"\n{state['phase']} score={state['score']} locked={state['locked']}"


def replay(path, render=False, step_mode=False, delay=0):
  from .engine import Engine, ENGINE
  header, steps = read(path)
  if header.get("engine_sha256") != hashlib.sha256(ENGINE.read_bytes()).hexdigest():
    print("Engine build differs; replay will verify every response.", file=sys.stderr)
  if step_mode and not sys.stdin.isatty():
    raise ValueError("--step requires an interactive terminal")
  last = None
  with Engine() as engine:
    for item in steps:
      actual = engine.exchange(item["request"])
      mismatch = difference(item["response"], actual)
      if mismatch:
        raise ValueError(f"Replay diverged at step {item['index']}: {mismatch}")
      last = actual
      if render or step_mode:
        print(f"Step {item['index']}: {encode(item['request'])}\n{board(actual['state'])}\n", file=sys.stderr)
      if step_mode:
        input("Enter: next step ")
      elif delay:
        time.sleep(delay)
  return {"verified": True, "steps": len(steps), "state": last["state"] if last else None}


def main():
  from .engine import Engine
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument("mode", choices=["record", "replay"])
  parser.add_argument("path", type=Path)
  parser.add_argument("--render", action="store_true")
  parser.add_argument("--step", action="store_true")
  parser.add_argument("--delay", type=float, default=0)
  args = parser.parse_args()
  if not 0 <= args.delay <= 60:
    parser.error("--delay must be between 0 and 60 seconds")
  try:
    if args.mode == "replay":
      print(encode(replay(args.path, args.render, args.step, args.delay)))
    else:
      args.path.parent.mkdir(parents=True, exist_ok=True)
      with args.path.open("x", encoding="utf-8") as trace, Engine(trace) as engine:
        for line in sys.stdin:
          request = json.loads(line)
          if not isinstance(request, dict):
            raise ValueError("Recording requires JSON objects")
          print(encode(engine.exchange(request)), flush=True)
  except (ValueError, OSError, RuntimeError) as error:
    print(str(error), file=sys.stderr)
    raise SystemExit(1) from error


if __name__ == "__main__":
  main()
