"""固定策略，比较不同颜色配置的生存分布；达到预算只记为截断，不视为通关。"""

import argparse
import hashlib
import json
import math
from pathlib import Path
import statistics

from .engine import Engine, ENGINE, ROOT
from .policy import BASELINE
from .train import evaluate, load, save, source_hash


def distribution(runs, max_pieces):
  lengths = sorted(run["locked"] for run in runs)
  def percentile(fraction):
    return lengths[max(0, math.ceil(len(lengths) * fraction) - 1)]
  return {
    "episodes": len(runs),
    "mean_locked_capped": statistics.mean(lengths),
    "p10_locked_capped": percentile(0.1),
    "median_locked_capped": statistics.median(lengths),
    "p90_locked_capped": percentile(0.9),
    "reached": {str(limit): sum(n >= limit for n in lengths) / len(runs)
      for limit in (50, 100, 150, 300) if limit <= max_pieces},
    "terminated": sum(not run["truncated"] for run in runs),
    "truncated": sum(run["truncated"] for run in runs),
    "mean_score": statistics.mean(run["score"] for run in runs),
    "mean_rarity_points": statistics.mean(run["rarity_points"] for run in runs),
    "gold_clear_episodes": sum(run["cleared_by_color"][6] > 0 for run in runs),
    "rejected_actions": sum(run["rejected_actions"] for run in runs)
  }


def run(model, seed, episodes, max_pieces):
  model_data = json.loads(model.read_text())
  seeds = list(range(seed, seed + episodes))
  if set(seeds) & set(model_data["training_seeds"]):
    raise ValueError("Evaluation seeds overlap training seeds")
  report = {"version": 1, "model": str(model), "model_sha256": hashlib.sha256(model.read_bytes()).hexdigest(),
    "engine_sha256": hashlib.sha256(ENGINE.read_bytes()).hexdigest(), "source_sha256": source_hash(),
    "seeds": seeds, "max_pieces": max_pieces, "profiles": {}}
  with Engine() as engine:
    for profile in ("classic", "rare_six", "rare_seven"):
      state = engine.command("start", mode="endless", seed=seed, color_profile=profile)["state"]
      entry = {"weights": state["color_weights"], "colors": state["colors"], "policies": {}}
      for name, weights in (("baseline", BASELINE), ("trained", load(model))):
        result = evaluate(engine, seeds, weights, max_pieces, profile)
        summary = distribution(result["runs"], max_pieces)
        entry["policies"][name] = {"summary": summary, "runs": result["runs"]}
        print(json.dumps({"profile": profile, "policy": name, **summary}), flush=True)
      report["profiles"][profile] = entry
  return report


def main():
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument("--model", type=Path, default=ROOT / "AI/models/starter.json")
  parser.add_argument("--seed", type=int, default=3001000)
  parser.add_argument("--episodes", type=int, default=30)
  parser.add_argument("--max-pieces", type=int, default=300)
  parser.add_argument("--report", type=Path, default=ROOT / "build/ai/color-pressure.json")
  args = parser.parse_args()
  if args.episodes < 1 or args.max_pieces < 1 or not 0 <= args.seed <= 2**31 - args.episodes:
    parser.error("Require positive budgets and seeds within nonnegative Int32")
  save(args.report, run(args.model, args.seed, args.episodes, args.max_pieces))


if __name__ == "__main__":
  main()
