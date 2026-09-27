"""固定小样本过拟合诊断；不作为泛化成绩。"""
import argparse
import json
import hashlib
from pathlib import Path
import torch
from .imitation import read_dataset, metrics
from .neural import ActorCritic
from .train import save


def run(dataset, validation, output, samples=256, epochs=200, seed=42):
  config, data = read_dataset(dataset)
  validation_config, heldout = read_dataset(validation)
  if set(config['seeds']) & set(validation_config['seeds']):
    raise ValueError('Train and validation episodes overlap')
  torch.manual_seed(seed)
  indices = torch.randperm(len(data[0]))[:samples]
  subset = tuple(t[indices] for t in data)
  model = ActorCritic()
  optimizer = torch.optim.Adam(model.parameters(), lr=3e-4)
  history = [{'epoch': 0, 'train': metrics(model, subset), 'validation': metrics(model, heldout)}]
  for epoch in range(epochs):
    for batch in torch.randperm(len(indices)).split(64):
      board, meta, mask, target = (t[batch] for t in subset)
      dist, _ = model(board.float(), meta, mask)
      loss = -(target * dist.logits).sum(-1).mean()
      optimizer.zero_grad()
      loss.backward()
      optimizer.step()
    if (epoch + 1) % 20 == 0:
      history.append({'epoch': epoch + 1, 'train': metrics(model, subset), 'validation': metrics(model, heldout)})
      print(json.dumps(history[-1]), flush=True)
  save(output, {'purpose': 'capacity_diagnostic_not_game_evaluation', 'seed': seed,
    'samples': len(indices), 'epochs': epochs, 'parameters': sum(p.numel() for p in model.parameters()),
    'dataset': str(dataset), 'training_seeds': config['seeds'],
    'validation_seeds': validation_config['seeds'], 'subset_indices': indices.tolist(),
    'dataset_sha256': hashlib.sha256(dataset.read_bytes()).hexdigest(),
    'validation_sha256': hashlib.sha256(validation.read_bytes()).hexdigest(),
    'torch_version': str(torch.__version__), 'history': history})


if __name__ == '__main__':
  parser = argparse.ArgumentParser(description=__doc__)
  parser.add_argument('--dataset', type=Path, required=True)
  parser.add_argument('--validation', type=Path, required=True)
  parser.add_argument('--output', type=Path, required=True)
  args = parser.parse_args()
  torch.set_num_threads(2)
  run(args.dataset, args.validation, args.output)
