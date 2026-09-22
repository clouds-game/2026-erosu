export type Cell = { x: number; y: number };
export type Block = {
  id: number;
  shape: string;
  color: number;
  cells: Cell[];
};
export type ClearWave = { blocks: Block[]; chain: number; points: number };

export const WIDTH = 10;
export const HEIGHT = 18;
export const COLORS = ['#ff8278', '#8ee2c4', '#f6ce71', '#aaa2f7'];
export const SHAPES: Record<string, Cell[]> = {
  I: [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 2, y: 0 }, { x: 3, y: 0 }],
  O: [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 0, y: 1 }, { x: 1, y: 1 }],
  T: [{ x: 1, y: 0 }, { x: 0, y: 1 }, { x: 1, y: 1 }, { x: 2, y: 1 }],
  L: [{ x: 2, y: 0 }, { x: 0, y: 1 }, { x: 1, y: 1 }, { x: 2, y: 1 }],
  J: [{ x: 0, y: 0 }, { x: 0, y: 1 }, { x: 1, y: 1 }, { x: 2, y: 1 }],
  S: [{ x: 1, y: 0 }, { x: 2, y: 0 }, { x: 0, y: 1 }, { x: 1, y: 1 }],
  Z: [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 1, y: 1 }, { x: 2, y: 1 }],
};

const key = (x: number, y: number) => `${x},${y}`;
export const shifted = (block: Block, dx: number, dy: number): Block => ({
  ...block,
  cells: block.cells.map(({ x, y }) => ({ x: x + dx, y: y + dy })),
});

export function matchingBlocks(blocks: Block[]): Block[] {
  const occupied = new Map<string, Block>();
  for (const block of blocks) {
    for (const cell of block.cells) occupied.set(key(cell.x, cell.y), block);
  }
  const visited = new Set<number>();
  const matches: Block[] = [];
  for (const block of blocks) {
    if (visited.has(block.id)) continue;
    const group = [block];
    visited.add(block.id);
    for (let i = 0; i < group.length; i++) {
      for (const { x, y } of group[i]!.cells) {
        for (const [dx, dy] of [[0, 1], [0, -1], [1, 0], [-1, 0]]) {
          const neighbor = occupied.get(key(x + dx!, y + dy!));
          if (neighbor && neighbor.color === block.color && !visited.has(neighbor.id)) {
            visited.add(neighbor.id);
            group.push(neighbor);
          }
        }
      }
    }
    if (group.length >= 3) matches.push(...group);
  }
  return matches;
}

// Move every unsupported rigid body together; propagate support from the floor.
// This also handles pieces whose bounding boxes interlock without overlapping.
export function gravityStep(blocks: Block[]): boolean {
  const occupied = new Map<string, number>();
  for (const block of blocks) {
    for (const cell of block.cells) occupied.set(key(cell.x, cell.y), block.id);
  }
  const movable = new Set(blocks.map(block => block.id));
  let changed = true;
  while (changed) {
    changed = false;
    for (const block of blocks) {
      if (!movable.has(block.id)) continue;
      const supported = block.cells.some(({ x, y }) => {
        const below = occupied.get(key(x, y + 1));
        return y + 1 >= HEIGHT || (below !== undefined && below !== block.id && !movable.has(below));
      });
      if (supported) {
        movable.delete(block.id);
        changed = true;
      }
    }
  }
  for (const block of blocks) {
    if (movable.has(block.id)) block.cells = shifted(block, 0, 1).cells;
  }
  return movable.size > 0;
}

export class Game {
  blocks: Block[] = [];
  active: Block | null = null;
  next: Block[] = [];
  score = 0;
  cleared = 0;
  best_chain = 0;
  locked = 0;
  over = false;
  private next_id = 1;
  private bag: string[] = [];
  private random: () => number;

  constructor(random: () => number = Math.random) {
    this.random = random;
    this.next = Array.from({ length: 3 }, () => this.makeBlock());
    this.spawn();
  }

  private makeBlock(): Block {
    if (!this.bag.length) {
      this.bag = Object.keys(SHAPES);
      for (let i = this.bag.length - 1; i > 0; i--) {
        const j = Math.floor(this.random() * (i + 1));
        [this.bag[i], this.bag[j]] = [this.bag[j]!, this.bag[i]!];
      }
    }
    const shape = this.bag.pop()!;
    return {
      id: this.next_id++, shape,
      color: Math.floor(this.random() * COLORS.length),
      cells: SHAPES[shape]!.map(cell => ({ ...cell })),
    };
  }

  valid(block: Block): boolean {
    const occupied = new Set(this.blocks.flatMap(piece => piece.cells.map(cell => key(cell.x, cell.y))));
    return block.cells.every(({ x, y }) => x >= 0 && x < WIDTH && y >= 0 && y < HEIGHT && !occupied.has(key(x, y)));
  }

  spawn(): void {
    const block = this.next.shift()!;
    this.next.push(this.makeBlock());
    const width = Math.max(...block.cells.map(cell => cell.x)) + 1;
    this.active = shifted(block, Math.floor((WIDTH - width) / 2), 0);
    if (!this.valid(this.active)) {
      this.active = null;
      this.over = true;
    }
  }

  move(dx: number, dy: number): boolean {
    if (!this.active || this.over) return false;
    const candidate = shifted(this.active, dx, dy);
    if (!this.valid(candidate)) return false;
    this.active = candidate;
    return true;
  }

  rotate(): boolean {
    if (!this.active || this.active.shape === 'O') return false;
    const left = Math.min(...this.active.cells.map(cell => cell.x));
    const top = Math.min(...this.active.cells.map(cell => cell.y));
    const height = Math.max(...this.active.cells.map(cell => cell.y)) - top + 1;
    const rotated = {
      ...this.active,
      cells: this.active.cells.map(({ x, y }) => ({ x: left + height - 1 - (y - top), y: top + x - left })),
    };
    for (const [dx, dy] of [[0, 0], [-1, 0], [1, 0], [-2, 0], [2, 0], [0, -1], [0, -2]]) {
      const candidate = shifted(rotated, dx!, dy!);
      if (this.valid(candidate)) {
        this.active = candidate;
        return true;
      }
    }
    return false;
  }

  ghost(): Block | null {
    if (!this.active) return null;
    let ghost = this.active;
    while (this.valid(shifted(ghost, 0, 1))) ghost = shifted(ghost, 0, 1);
    return ghost;
  }

  lock(): void {
    if (!this.active) return;
    this.blocks.push(this.active);
    this.active = null;
    this.locked++;
  }

  clear(chain: number): ClearWave | null {
    const matches = matchingBlocks(this.blocks);
    if (!matches.length) return null;
    const ids = new Set(matches.map(block => block.id));
    this.blocks = this.blocks.filter(block => !ids.has(block.id));
    const points = matches.length * 100 * chain;
    this.score += points;
    this.cleared += matches.length;
    this.best_chain = Math.max(this.best_chain, chain);
    return { blocks: matches, chain, points };
  }
}
