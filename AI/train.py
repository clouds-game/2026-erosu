"""CPU 交叉熵策略训练、独立种子评估和 JSON 对局记录。仅依赖 Python 标准库。"""

import argparse
import hashlib
import json
import math
from pathlib import Path
import random
import statistics
import subprocess

from .engine import Engine, ROOT
from .policy import BASELINE, FEATURES, choose


def episode(engine, seed, weights, max_pieces):
  state = engine.command("start", mode="endless", seed=seed)["state"]
  rng = random.Random(seed)
  rejected = 0
  while not state["is_finished"] and state["locked"] < max_pieces:
    rotation, left = choose(state, weights, rng)
    for _ in range(rotation):
      response = engine.command("rotate")
      rejected += not response["applied"]
      state = response["state"]
    current = min(cell["x"] for cell in state["active"]["cells"])
    for _ in range(abs(left - current)):
      response = engine.command("move", direction="left" if left < current else "right")
      state = response["state"]
      if not response["applied"]:
        rejected += 1
        break
    state = engine.command("hard_drop")["state"]
    # Stop exactly at the next decision point, never tick the next falling piece accidentally.
    for _ in range(3600):
      if state["phase"] not in ("clearing", "settling"):
        break
      state = engine.command("tick", count=1)["state"]
    else:
      raise RuntimeError("Resolution exceeded 3600 ticks")
  return {"seed": seed, "score": state["score"], "locked": state["locked"],
    "cleared": state["cleared"], "best_chain": state["best_chain"],
    "truncated": not state["is_finished"], "rejected_actions": rejected}


def evaluate(engine, seeds, weights, max_pieces):
  runs = [episode(engine, seed, weights, max_pieces) for seed in seeds]
  return {"mean_score": statistics.mean(run["score"] for run in runs),
    "mean_locked": statistics.mean(run["locked"] for run in runs),
    "runs": runs}


def save(path, value):
  path.parent.mkdir(parents=True, exist_ok=True)
  path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def load(path):
  data = json.loads(path.read_text())
  weights = data["weights"]
  if data["version"] != 1 or data["features"] != FEATURES or len(weights) != len(FEATURES) or not all(math.isfinite(w) for w in weights):
    raise ValueError("Incompatible model features or non-finite weights")
  return weights


def source_hash():
  digest = hashlib.sha256()
  for folder, suffix in [("Core", "*.cs"), ("Headless", "*.cs"), ("AI", "*.py")]:
    for path in sorted((ROOT / folder).glob(suffix)):
      digest.update(path.relative_to(ROOT).as_posix().encode())
      digest.update(path.read_bytes())
  return digest.hexdigest()


def main():
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument("mode", choices=["train", "evaluate", "play"])
  parser.add_argument("--model", type=Path, default=Path("build/ai/model.json"))
  parser.add_argument("--report", type=Path, default=Path("build/ai/evaluation.json"))
  parser.add_argument("--trace", type=Path, default=Path("build/ai/game.jsonl"))
  parser.add_argument("--seed", type=int, default=42)
  parser.add_argument("--generations", type=int, default=5)
  parser.add_argument("--population", type=int, default=12)
  parser.add_argument("--episodes", type=int, default=4)
  parser.add_argument("--max-pieces", type=int, default=150)
  args = parser.parse_args()
  if min(args.generations, args.episodes, args.max_pieces) < 1 or args.population < 4:
    parser.error("Positive budgets and population >= 4 required")
  if not 0 <= args.seed <= 2**31 - 1 - args.episodes:
    parser.error("seed must leave room for episodes within a nonnegative Int32")
  if args.mode == "play":
    args.trace.parent.mkdir(parents=True, exist_ok=True)
    with args.trace.open("w", encoding="utf-8") as trace, Engine(trace) as engine:
      print(json.dumps(episode(engine, args.seed, load(args.model), args.max_pieces)))
    return
  with Engine() as engine:
    if args.mode == "train":
      rng = random.Random(args.seed)
      # Fixed training seeds make candidate scores comparable; test seeds never select a model.
      seeds = rng.sample(range(1_000_000), args.episodes)
      mean, sigma = BASELINE[:], [1.5] * len(FEATURES)
      best, best_score = mean[:], -math.inf
      history = []
      for generation in range(args.generations):
        population = [best[:]] + [[rng.gauss(m, s) for m, s in zip(mean, sigma)]
          for _ in range(args.population - 1)]
        ranked = []
        for weights in population:
          result = evaluate(engine, seeds, weights, args.max_pieces)
          fitness = result["mean_score"] / 1000 + result["mean_locked"] * 0.1
          ranked.append((fitness, weights))
        ranked.sort(key=lambda item: item[0], reverse=True)
        if ranked[0][0] > best_score:
          best_score, best = ranked[0]
        elite = [weights for _, weights in ranked[:max(2, args.population // 4)]]
        mean = [statistics.mean(values) for values in zip(*elite)]
        sigma = [max(0.15, statistics.pstdev(values)) for values in zip(*elite)]
        history.append({"generation": generation + 1, "best_fitness": best_score})
        save(args.model, {"version": 1, "features": FEATURES, "weights": best,
          "algorithm": "cross_entropy", "training_seeds": seeds,
          "training_seed": args.seed, "max_pieces": args.max_pieces,
          "population": args.population, "history": history,
          "source_sha256": source_hash(),
          "git_commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()})
        print(json.dumps(history[-1]), flush=True)
    else:
      model = json.loads(args.model.read_text())
      seeds = list(range(args.seed, args.seed + args.episodes))
      if set(seeds) & set(model["training_seeds"]):
        parser.error("Evaluation seeds overlap training seeds")
      weights = load(args.model)
      report = {"model": str(args.model), "seeds": seeds, "max_pieces": args.max_pieces}
      for name, policy in [("random", None), ("baseline", BASELINE), ("trained", weights)]:
        report[name] = evaluate(engine, seeds, policy, args.max_pieces)
        print(json.dumps({"policy": name, "mean_score": report[name]["mean_score"],
          "mean_locked": report[name]["mean_locked"]}), flush=True)
      save(args.report, report)


if __name__ == "__main__":
  main()
