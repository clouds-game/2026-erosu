import './style.css';
import { Game, COLORS, WIDTH, HEIGHT, SHAPES, gravityStep, shifted } from './engine.ts';
import type { Block, ClearWave } from './engine.ts';

document.querySelector<HTMLDivElement>('#app')!.innerHTML = `
  <header class="site-header">
    <a class="brand" href="./" aria-label="Chroma Drop home"><img src="/assets/mark.svg" alt="" /> tiny games club<span>/ 001</span></a>
    <span class="prototype"><i></i> PLAYABLE EXPERIMENT</span>
  </header>
  <main>
    <section class="intro">
      <p class="eyebrow">FALL INTO PLACE. FIND YOUR COLOR.</p>
      <h1>CHROMA<span>DROP<span class="title-dot">✳</span></span></h1>
      <p class="lede">Familiar shapes.<br />A different kind of connection.</p>
      <div class="rule-card">
        <span class="step-label">THE LITTLE TWIST</span>
        <div class="mini-match" aria-hidden="true"><span>┛</span><b>+</b><span>━</span><b>+</b><span>▟</span><b>=</b><em>✦</em></div>
        <h2>3 blocks. 1 color. Pop.</h2>
        <p>Connect three whole blocks of the same color, edge to edge. Any connected shape counts.</p>
        <p class="rule-note">Count blocks, not little squares.</p>
      </div>
      <div class="detail"><span>01</span><p>Place your shape.<br /><small>Rotate it. Find a good home.</small></p></div>
      <div class="detail"><span>02</span><p>Make a connection.<br /><small>Match 3+ blocks of one color.</small></p></div>
      <div class="detail"><span>03</span><p>Let the chain happen.<br /><small>Whole shapes fall after a match.</small></p></div>
      <button id="demo" class="text-button">Try a match <span>↗</span></button>
    </section>
    <section class="game-column" aria-label="Game">
      <div class="board-top"><span><i></i> THE PLAYFIELD</span><span id="level">LEVEL 01</span></div>
      <div class="board-wrap">
        <canvas id="board" width="640" height="1152" aria-label="Ten-column falling block game. Use arrow keys to move and rotate, and space to drop."></canvas>
        <div id="overlay" class="overlay">
          <div class="overlay-symbol">✳</div><span class="eyebrow">A NEW WAY TO CONNECT</span>
          <h2>A little color.<br />A little gravity.</h2>
          <p>Three matching blocks.<br />One satisfying chain reaction.</p>
          <button id="start" class="primary-button">Let’s play <span>→</span></button>
          <small>or press Enter</small>
        </div>
        <div id="toast" class="toast" role="status" aria-live="polite"></div>
      </div>
      <div class="touch-controls" aria-label="Game controls">
        <button data-action="left" aria-label="Move left">←</button><button data-action="rotate" aria-label="Rotate">↻</button><button data-action="right" aria-label="Move right">→</button><button data-action="down" aria-label="Move down">↓</button><button data-action="drop" class="drop-button">DROP ↓</button>
      </div>
      <p class="board-caption">A good connection changes everything.</p>
    </section>
    <aside>
      <div class="score-card"><span class="step-label">YOUR SCORE</span><strong id="score">00000</strong><div class="best-line">PERSONAL BEST <span id="best">0</span></div></div>
      <div class="next-card"><span class="step-label">COMING UP</span><canvas id="next" width="400" height="360" aria-label="Next three colored shapes"></canvas><span class="next-note">A little room for planning.</span></div>
      <div class="stats"><div><span>Blocks cleared</span><b id="cleared">0</b></div><div><span>Best chain</span><b id="chain">—</b></div></div>
      <div class="controls"><span class="step-label">AT YOUR FINGERTIPS</span><div><span>Move</span><span><kbd>←</kbd> <kbd>→</kbd></span></div><div><span>Rotate</span><kbd>↑</kbd></div><div><span>Soft drop</span><kbd>↓</kbd></div><div><span>Hard drop</span><kbd>space</kbd></div><div><span>Pause</span><kbd>P</kbd></div></div>
      <div class="utility"><button id="pause" aria-label="Pause game">Ⅱ Pause</button><button id="sound" aria-pressed="false">♫ Sound off</button></div>
      <button id="restart" class="restart-button">↻ &nbsp; Start fresh</button>
    </aside>
  </main>
  <footer><span>LESS STACKING. MORE CONNECTING.</span><span>Prototype 01 <span class="footer-dot">·</span> Made for a little play.</span></footer>
`;

const el = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const canvas = el<HTMLCanvasElement>('board');
const ctx = canvas.getContext('2d')!;
const preview = el<HTMLCanvasElement>('next').getContext('2d')!;
const unit = 64;
let game = new Game();
let phase: 'ready' | 'falling' | 'flash' | 'gravity' | 'over' = 'ready';
let paused = false;
let timer = 0;
let lock_timer = 0;
let chain = 0;
let wave: ClearWave | null = null;
let toast_timer = 0;
let best = 0;
let sound_enabled = false;
let audio: AudioContext | null = null;
try { best = Number(localStorage.getItem('chroma_best')) || 0; } catch { /* Storage is optional. */ }

function playSound(frequency: number, duration = 0.1): void {
  if (!sound_enabled) return;
  audio ??= new AudioContext();
  void audio.resume();
  const oscillator = audio.createOscillator();
  const gain = audio.createGain();
  oscillator.type = 'sine';
  oscillator.frequency.setValueAtTime(frequency, audio.currentTime);
  gain.gain.setValueAtTime(0.07, audio.currentTime);
  gain.gain.exponentialRampToValueAtTime(0.001, audio.currentTime + duration);
  oscillator.connect(gain).connect(audio.destination);
  oscillator.start();
  oscillator.stop(audio.currentTime + duration);
}

function drawBlock(context: CanvasRenderingContext2D, block: Block, size: number, ghost = false): void {
  const own = new Set(block.cells.map(({ x, y }) => `${x},${y}`));
  context.save();
  context.fillStyle = COLORS[block.color]!;
  context.globalAlpha = ghost ? 0.12 : 1;
  for (const { x, y } of block.cells) context.fillRect(x * size, y * size, size, size);
  context.globalAlpha = ghost ? 0.55 : 1;
  context.strokeStyle = ghost ? COLORS[block.color]! : '#252d3b';
  context.lineWidth = ghost ? 2 : 5;
  context.lineCap = 'round';
  if (ghost) context.setLineDash([5, 5]);
  for (const { x, y } of block.cells) {
    const px = x * size;
    const py = y * size;
    for (const [dx, dy, x1, y1, x2, y2] of [
      [0, -1, px, py + 1, px + size, py + 1],
      [0, 1, px, py + size - 1, px + size, py + size - 1],
      [-1, 0, px + 1, py, px + 1, py + size],
      [1, 0, px + size - 1, py, px + size - 1, py + size],
    ]) {
      if (!own.has(`${x + dx},${y + dy}`)) {
        context.beginPath(); context.moveTo(x1, y1); context.lineTo(x2, y2); context.stroke();
      }
    }
    if (!ghost) {
      context.save();
      context.fillStyle = '#252d3b';
      context.strokeStyle = '#252d3b';
      context.globalAlpha = 0.3;
      context.lineWidth = 2;
      const cx = px + size / 2;
      const cy = py + size / 2;
      context.beginPath();
      if (block.color === 0) { context.arc(cx, cy, 3, 0, Math.PI * 2); context.fill(); }
      else if (block.color === 1) { context.rect(cx - 3, cy - 3, 6, 6); context.stroke(); }
      else if (block.color === 2) { context.moveTo(cx - 4, cy); context.lineTo(cx + 4, cy); context.moveTo(cx, cy - 4); context.lineTo(cx, cy + 4); context.stroke(); }
      else { context.moveTo(cx - 3, cy + 4); context.lineTo(cx + 3, cy - 4); context.stroke(); }
      context.restore();
    }
  }
  context.restore();
}

function render(): void {
  ctx.fillStyle = '#252d3b';
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  ctx.fillStyle = '#3c4350';
  for (let x = 0; x < WIDTH; x++) {
    for (let y = 0; y < HEIGHT; y++) {
      ctx.beginPath(); ctx.arc(x * unit + unit / 2, y * unit + unit / 2, 1.7, 0, Math.PI * 2); ctx.fill();
    }
  }
  for (const block of game.blocks) drawBlock(ctx, block, unit);
  if (phase === 'falling') {
    const ghost = game.ghost();
    if (ghost) drawBlock(ctx, ghost, unit, true);
    if (game.active) drawBlock(ctx, game.active, unit);
  }
  if (phase === 'flash' && wave) {
    for (const block of wave.blocks) {
      drawBlock(ctx, block, unit);
      ctx.fillStyle = `rgba(255,255,255,${0.25 + 0.4 * Math.sin(timer / 45)})`;
      for (const { x, y } of block.cells) ctx.fillRect(x * unit + 3, y * unit + 3, unit - 6, unit - 6);
    }
  }
}

function updateStats(): void {
  el('score').textContent = game.score.toString().padStart(5, '0');
  el('cleared').textContent = game.cleared.toString();
  el('chain').textContent = game.best_chain ? `×${game.best_chain}` : '—';
  el('level').textContent = `LEVEL ${Math.min(10, 1 + Math.floor(game.locked / 15)).toString().padStart(2, '0')}`;
  if (game.score > best) {
    best = game.score;
    try { localStorage.setItem('chroma_best', best.toString()); } catch { /* Storage is optional. */ }
  }
  el('best').textContent = best.toLocaleString();
  preview.clearRect(0, 0, 400, 360);
  game.next.forEach((block, i) => {
    const width = Math.max(...block.cells.map(cell => cell.x)) + 1;
    preview.save();
    preview.translate(104 + (4 - width) * 20, 14 + i * 118);
    drawBlock(preview, block, 40);
    preview.restore();
    preview.fillStyle = '#8a8d8b';
    preview.font = '20px monospace';
    preview.fillText(`0${i + 1}`, 20, 60 + i * 118);
  });
}

function toast(message: string): void {
  el('toast').textContent = message;
  el('toast').classList.add('visible');
  toast_timer = 1800;
}

function showOverlay(title: string, copy: string, button: string): void {
  const overlay = el('overlay');
  overlay.innerHTML = `<div class="overlay-symbol">✳</div><h2>${title}</h2><p>${copy}</p><button id="start" class="primary-button">${button} <span>→</span></button><small>or press Enter</small>`;
  overlay.hidden = false;
  el('start').addEventListener('click', () => paused ? togglePause() : start());
}

function start(demo = false): void {
  game = new Game();
  phase = 'falling';
  paused = false;
  timer = 0;
  lock_timer = 0;
  chain = 0;
  wave = null;
  toast_timer = 0;
  el('toast').classList.remove('visible');
  el('overlay').hidden = true;
  el('pause').textContent = 'Ⅱ Pause';
  el('pause').setAttribute('aria-label', 'Pause game');
  if (demo) {
    // Negative IDs keep the demonstration separate from generated blocks.
    game.blocks = [0, 2].map((x, i) => ({ id: -1 - i, shape: 'O', color: 0, cells: SHAPES.O.map(cell => ({ x: cell.x + x, y: cell.y + 16 })) }));
    game.active = { id: -3, shape: 'O', color: 0, cells: SHAPES.O.map(cell => ({ x: cell.x + 4, y: cell.y })) };
    toast('Press SPACE or DROP. Three blocks, one match.');
  }
  updateStats();
}

function nextTurn(): void {
  game.spawn();
  timer = 0;
  lock_timer = 0;
  phase = game.over ? 'over' : 'falling';
  updateStats();
  if (game.over) showOverlay('Room for<br />one more try?', `${game.score.toLocaleString()} points · ${game.cleared} blocks connected`, 'Play again');
}

function checkMatches(): void {
  wave = game.clear(chain + 1);
  timer = 0;
  if (wave) {
    chain++;
    phase = 'flash';
    toast(`${chain > 1 ? `CHAIN ×${chain}` : 'NICE CONNECTION'} · ${wave.blocks.length} blocks · +${wave.points}`);
    playSound(360 + chain * 140, 0.25);
    updateStats();
  } else nextTurn();
}

function lock(): void {
  game.lock();
  chain = 0;
  playSound(160, 0.08);
  checkMatches();
}

function togglePause(): void {
  if (phase === 'ready' || phase === 'over') return;
  paused = !paused;
  el('pause').textContent = paused ? '▶ Resume' : 'Ⅱ Pause';
  el('pause').setAttribute('aria-label', paused ? 'Resume game' : 'Pause game');
  if (paused) showOverlay('Take a<br />little breather.', 'Your colors will be right here.', 'Keep playing');
  else el('overlay').hidden = true;
}

function action(command: string): void {
  if (paused || phase !== 'falling') return;
  if (command === 'drop') {
    game.active = game.ghost();
    lock();
  } else if (command === 'rotate') {
    if (game.rotate()) playSound(240, 0.04);
  } else {
    const moved = game.move(command === 'left' ? -1 : command === 'right' ? 1 : 0, command === 'down' ? 1 : 0);
    if (moved && command === 'down') timer = 0;
  }
}

document.addEventListener('keydown', event => {
  if (event.target instanceof HTMLButtonElement && (event.code === 'Space' || event.code === 'Enter')) return;
  const commands: Record<string, string> = { ArrowLeft: 'left', ArrowRight: 'right', ArrowUp: 'rotate', ArrowDown: 'down', Space: 'drop', KeyA: 'left', KeyD: 'right', KeyW: 'rotate', KeyS: 'down' };
  if (commands[event.code]) {
    event.preventDefault();
    if (event.repeat && ['drop', 'rotate'].includes(commands[event.code])) return;
    action(commands[event.code]);
  }
  if ((event.code === 'KeyP' || event.code === 'Escape') && !event.repeat) togglePause();
  if (event.code === 'Enter' && !event.repeat) {
    if (paused) togglePause();
    else if (phase === 'ready' || phase === 'over') start();
  }
});
el('start').addEventListener('click', () => start());
el('restart').addEventListener('click', () => start());
el('demo').addEventListener('click', () => start(true));
el('pause').addEventListener('click', togglePause);
el('sound').addEventListener('click', () => {
  sound_enabled = !sound_enabled;
  el('sound').textContent = sound_enabled ? '♫ Sound on' : '♫ Sound off';
  el('sound').setAttribute('aria-pressed', String(sound_enabled));
  playSound(500);
});
document.querySelectorAll<HTMLButtonElement>('[data-action]').forEach(button => {
  button.addEventListener('click', () => { action(button.dataset.action!); button.blur(); });
});
// Avoid leaving a focused utility button bound to the hard-drop space key.
document.querySelectorAll<HTMLButtonElement>('button').forEach(button => {
  button.addEventListener('pointerup', () => button.blur());
});
window.addEventListener('blur', () => { if (!paused && phase !== 'ready' && phase !== 'over') togglePause(); });

let previous = performance.now();
function frame(now: number): void {
  const dt = Math.min(60, now - previous);
  previous = now;
  if (!paused && phase !== 'ready' && phase !== 'over') {
    timer += dt;
    toast_timer -= dt;
    if (toast_timer <= 0) el('toast').classList.remove('visible');
    if (phase === 'falling') {
      const interval = Math.max(180, 850 - Math.floor(game.locked / 15) * 70);
      if (timer >= interval) { game.move(0, 1); timer = 0; }
      if (game.active && !game.valid(shifted(game.active, 0, 1))) {
        lock_timer += dt;
        if (lock_timer >= 380) lock();
      } else lock_timer = 0;
    } else if (phase === 'flash' && timer >= 320) {
      phase = 'gravity'; timer = 0; wave = null;
    } else if (phase === 'gravity' && timer >= 45) {
      timer = 0;
      if (!gravityStep(game.blocks)) checkMatches();
    }
  }
  render();
  requestAnimationFrame(frame);
}
updateStats();
requestAnimationFrame(frame);
