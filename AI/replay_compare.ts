(() => {
  type Cell = { x: number; y: number };
  type Piece = { color: number; cells: Cell[] };
  type Frame = {
    pieces: Piece[]; active: Piece | null; wave: { pieces: Piece[] } | null;
    phase: string; score: number; locked: number; cleared: number; best_chain: number; repeat?: number;
  };
  type Result = { score: number; locked: number; best_chain: number; capped?: boolean; truncated?: boolean };
  type Policy = { name: string; result: Result; turns: Frame[][] };
  type Scenario = { seed: number; policies: Policy[] };
  type Benchmark = { name: string; mean_locked: number; mean_score: number; gold_clear_episodes: number };
  type ReplayData = {
    title?: string; seed: number; max_pieces: number; colors: { hex: string; mark: string }[];
    policies: Policy[]; scenarios?: Scenario[]; benchmark?: Benchmark[]; benchmark_episodes?: number;
    benchmark_summary?: string;
  };
  function element<T extends HTMLElement>(id: string): T {
    const result = document.getElementById(id);
    if (!result) throw new Error(`Missing element: ${id}`);
    return result as T;
  }
  const data: ReplayData = JSON.parse(element('replay-data').textContent ?? '');
  const scenarios = data.scenarios ?? [{ seed: data.seed, policies: data.policies }];
  const colors = data.colors.map(color => color.hex);
  const marks = data.colors.map(color => color.mark);
  const ids = ['before', 'after'];
  let scenario = scenarios[0], selected = [0, 1];
  let turn = 0, fraction = 0, playing = false, last = 0, total = 0;
  const slider = element<HTMLInputElement>('position');
  const seedSelect = element<HTMLSelectElement>('seed');
  const selectors = ids.map(id => element<HTMLSelectElement>(`${id}-policy`));
  const title = data.title ?? 'Before / after training';
  document.title = title;
  element('title').textContent = title;
  scenarios.forEach((s, index) => seedSelect.add(new Option(String(s.seed), String(index))));
  seedSelect.disabled = scenarios.length === 1;
  if (data.benchmark) {
    element('benchmark').hidden = false;
    element('benchmark-summary').textContent = data.benchmark_summary ?? `${data.benchmark_episodes} independent evaluation seeds`;
    data.benchmark.forEach(row => {
      const tr = document.createElement('tr');
      [row.name, row.mean_locked.toFixed(1), Math.round(row.mean_score).toLocaleString(),
        `${row.gold_clear_episodes}/${data.benchmark_episodes}`].forEach(value => {
        const td = document.createElement('td');
        td.textContent = value;
        tr.append(td);
      });
      element('benchmark-rows').append(tr);
    });
  }
  function piece(ctx: CanvasRenderingContext2D, p: Piece, alpha = 1): void {
    ctx.globalAlpha = alpha;
    ctx.fillStyle = colors[p.color];
    const cells = new Set(p.cells.map(c => `${c.x},${c.y}`));
    for (const c of p.cells) ctx.fillRect(c.x * 30, c.y * 30, 30, 30);
    ctx.strokeStyle = '#101319';
    ctx.lineWidth = 3;
    for (const c of p.cells) {
      const x = c.x * 30, y = c.y * 30;
      for (const [dx, dy, ax, ay, bx, by] of [[0,-1,x,y,x+30,y],[0,1,x,y+30,x+30,y+30],[-1,0,x,y,x,y+30],[1,0,x+30,y,x+30,y+30]]) {
        if (!cells.has(`${c.x+dx},${c.y+dy}`)) {
          ctx.beginPath(); ctx.moveTo(ax,ay); ctx.lineTo(bx,by); ctx.stroke();
        }
      }
    }
    const first = [...p.cells].sort((a,b) => a.y-b.y || a.x-b.x)[0];
    ctx.fillStyle = '#101319'; ctx.font = '17px system-ui'; ctx.textAlign = 'center';
    ctx.fillText(marks[p.color], first.x*30+15, first.y*30+21);
    ctx.globalAlpha = 1;
  }
  function currentFrame(policy: Policy): Frame {
    const group = policy.turns[Math.min(turn, policy.turns.length - 1)];
    if (turn >= policy.turns.length || fraction >= 1) return group[group.length - 1];
    // Repeated identical snapshots are compacted without shortening their playback interval.
    const duration = group.reduce((sum, s) => sum + (s.repeat ?? 1), 0);
    let position = fraction * duration;
    for (const s of group) {
      position -= s.repeat ?? 1;
      if (position < 0) return s;
    }
    return group[group.length - 1];
  }
  function draw(): void {
    selected.forEach((selection, i) => {
      const p = scenario.policies[selection], s = currentFrame(p);
      const ctx = element<HTMLCanvasElement>(ids[i]).getContext('2d');
      if (!ctx) throw new Error('Canvas is unavailable');
      ctx.clearRect(0,0,300,540);
      for (const block of s.pieces) piece(ctx, block);
      if (s.wave) for (const block of s.wave.pieces) piece(ctx, block, 0.4);
      if (s.active) piece(ctx, s.active, 0.75);
      element(`${ids[i]}-stats`).textContent = `${s.score.toLocaleString()} points · ${s.locked} placed · ${s.cleared} cleared · chain ${s.best_chain} · ${s.phase}`;
    });
    slider.value = String(turn);
    element('turn').textContent = `Piece ${turn+1} / ${total}`;
    element('play').textContent = playing ? 'Pause' : 'Play';
  }
  function selectComparison(): void {
    playing = false; turn = 0; fraction = 0;
    total = Math.max(...selected.map(index => scenario.policies[index].turns.length));
    slider.max = String(total - 1);
    element('meta').textContent = `Seed ${scenario.seed} · ${data.max_pieces}-piece limit · verified engine replays`;
    selected.forEach((selection, i) => {
      const r = scenario.policies[selection].result;
      element(`${ids[i]}-result`).textContent = `Final: ${r.locked} pieces · ${r.score.toLocaleString()} points · chain ${r.best_chain} · ${r.capped || r.truncated ? 'limit reached' : 'game over'}`;
    });
    draw();
  }
  function chooseScenario(index: number): void {
    scenario = scenarios[index];
    selectors.forEach((select, i) => {
      select.replaceChildren();
      scenario.policies.forEach((policy, index) => select.add(new Option(policy.name, String(index))));
      select.value = String(selected[i]);
    });
    selectComparison();
  }
  function seek(value: number): void {
    playing = false; turn = Math.max(0, Math.min(total - 1, value)); fraction = 0; draw();
  }
  selectors.forEach((select, i) => select.onchange = () => {
    selected[i] = Number(select.value); selectComparison();
  });
  seedSelect.onchange = () => chooseScenario(Number(seedSelect.value));
  slider.oninput = () => seek(Number(slider.value));
  element('back').onclick = () => seek(turn - 1);
  element('forward').onclick = () => seek(turn + 1);
  element('end').onclick = () => { seek(total - 1); fraction = 1; draw(); };
  element('play').onclick = () => {
    if (turn === total - 1 && fraction >= 1) { turn = 0; fraction = 0; }
    playing = !playing; draw();
  };
  function animate(now: number): void {
    if (playing && last) {
      fraction += Math.min(now-last,100)/1600 * Number(element<HTMLSelectElement>('speed').value);
      if (fraction >= 1) {
        if (turn < total - 1) { turn++; fraction = 0; }
        else { fraction = 1; playing = false; }
      }
      draw();
    }
    last = now; requestAnimationFrame(animate);
  }
  chooseScenario(0);
  requestAnimationFrame(animate);
})();
