import test from 'node:test';
import assert from 'node:assert/strict';
import { Game, HEIGHT, SHAPES, matchingBlocks, gravityStep, shifted } from '../src/engine.ts';
import type { Block } from '../src/engine.ts';

function block(id: number, shape: string, x: number, y: number, color = 0): Block {
  return shifted({ id, shape, color, cells: SHAPES[shape].map(cell => ({ ...cell })) }, x, y);
}

test('a tetromino with four squares is only one block, and two touching blocks do not clear', () => {
  assert.equal(matchingBlocks([block(1, 'I', 0, 17)]).length, 0);
  assert.equal(matchingBlocks([block(1, 'O', 0, 16), block(2, 'O', 2, 16)]).length, 0);
});

test('three edge-connected blocks match without needing a straight line', () => {
  const blocks = [block(1, 'O', 0, 16), block(2, 'O', 2, 16), block(3, 'O', 0, 14)];
  assert.equal(matchingBlocks(blocks).length, 3);
});

test('diagonal contact and different colors do not connect groups', () => {
  assert.equal(matchingBlocks([block(1, 'O', 0, 16), block(2, 'O', 2, 14), block(3, 'O', 4, 12)]).length, 0);
  assert.equal(matchingBlocks([block(1, 'O', 0, 16), block(2, 'O', 2, 16, 1), block(3, 'O', 4, 16)]).length, 0);
});

test('a full row of fewer than three matching blocks stays on the board', () => {
  const game = new Game();
  game.blocks = [block(1, 'I', 0, 17), block(2, 'I', 4, 17, 1), block(3, 'O', 8, 16, 2)];
  assert.equal(game.clear(1), null);
  assert.equal(game.blocks.length, 3);
});

test('gravity preserves the complete shape and stops on partial support', () => {
  const blocks = [block(1, 'O', 0, 16, 1), block(2, 'I', 1, 3)];
  while (gravityStep(blocks)) { /* settle */ }
  assert.deepEqual(blocks[1].cells, [{ x: 1, y: 15 }, { x: 2, y: 15 }, { x: 3, y: 15 }, { x: 4, y: 15 }]);
});

test('stacked unsupported blocks fall together, independent of array order', () => {
  for (const reverse of [false, true]) {
    const blocks = [block(1, 'O', 0, 8), block(2, 'O', 0, 6)];
    if (reverse) blocks.reverse();
    while (gravityStep(blocks)) { /* settle */ }
    assert.equal(Math.min(...blocks.find(piece => piece.id === 1)!.cells.map(cell => cell.y)), 16);
    assert.equal(Math.min(...blocks.find(piece => piece.id === 2)!.cells.map(cell => cell.y)), 14);
  }
});

test('clearing support causes a second wave, with whole-block scoring', () => {
  const game = new Game();
  game.blocks = [
    block(10, 'O', 0, 16), block(11, 'O', 2, 16), block(12, 'O', 4, 16),
    block(13, 'O', 0, 14, 1), block(14, 'O', 2, 12, 1), block(15, 'O', 4, 10, 1),
  ];
  assert.equal(game.clear(1)?.blocks.length, 3);
  while (gravityStep(game.blocks)) { /* settle */ }
  assert.equal(game.clear(2)?.blocks.length, 3);
  assert.equal(game.score, 900);
  assert.equal(game.best_chain, 2);
  assert.equal(game.cleared, 6);
  assert.deepEqual(game.blocks, []);
});

test('ghost, rotation and movement respect walls, floor and occupied squares', () => {
  const game = new Game();
  game.active = block(40, 'I', 0, 17);
  assert.equal(game.move(-1, 0), false);
  assert.equal(game.move(0, 1), false);
  game.active = block(41, 'T', 0, 0);
  assert.equal(game.rotate(), true);
  assert.ok(game.valid(game.active!));
  const ghost = game.ghost()!;
  assert.equal(Math.max(...ghost.cells.map(cell => cell.y)), HEIGHT - 1);
  game.blocks = [block(42, 'O', 0, 16)];
  assert.ok(game.ghost()!.cells.every(cell => cell.y < 16));
});

test('blocked spawn ends the game', () => {
  const game = new Game();
  const next = game.next[0];
  const width = Math.max(...next.cells.map(cell => cell.x)) + 1;
  game.blocks = [shifted({ ...next, id: -1 }, Math.floor((10 - width) / 2), 0)];
  game.spawn();
  assert.equal(game.over, true);
  assert.equal(game.active, null);
});

test('each seven-piece bag includes every shape once', () => {
  const game = new Game();
  const shapes = [game.active!.shape];
  for (let i = 0; i < 6; i++) { game.spawn(); shapes.push(game.active!.shape); }
  assert.equal(new Set(shapes).size, 7);
});
