export type DotNetCallback = { invokeMethodAsync: (method: string, ...args: unknown[]) => Promise<unknown> };
const game_keys = new Set(['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'KeyA', 'KeyD', 'KeyW', 'KeyS', 'Space', 'KeyP', 'Escape', 'KeyR', 'F2', 'KeyM', 'Enter']);
let detach: (() => void) | undefined;
let audio: AudioContext | undefined;

function ignoresKeyboard(target: EventTarget | null): boolean {
  return target instanceof Element && (
    target.closest('input, textarea, select, [role="textbox"]') !== null ||
    target instanceof HTMLElement && target.isContentEditable
  );
}

function isButton(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest('button') !== null;
}

export function connect(callback: DotNetCallback): void {
  detach?.();
  const keydown = (event: KeyboardEvent) => {
    if (ignoresKeyboard(event.target)) return;
    if (!game_keys.has(event.code) || event.ctrlKey || event.metaKey || event.altKey) return;
    if (isButton(event.target) && ['Enter', 'Space'].includes(event.code)) return;
    event.preventDefault();
    if (!event.repeat) void callback.invokeMethodAsync('KeyChanged', event.code, true);
  };
  const keyup = (event: KeyboardEvent) => {
    if (game_keys.has(event.code)) void callback.invokeMethodAsync('KeyChanged', event.code, false);
  };
  const blur = () => { void callback.invokeMethodAsync('PauseForFocus'); };
  const visibility = () => { if (document.hidden) blur(); };
  const unlock = () => { if (audio?.state === 'suspended') void audio.resume(); };
  window.addEventListener('keydown', keydown);
  window.addEventListener('keyup', keyup);
  window.addEventListener('blur', blur);
  document.addEventListener('visibilitychange', visibility);
  window.addEventListener('pointerdown', unlock);
  window.addEventListener('keydown', unlock);
  detach = () => {
    window.removeEventListener('keydown', keydown);
    window.removeEventListener('keyup', keyup);
    window.removeEventListener('blur', blur);
    document.removeEventListener('visibilitychange', visibility);
    window.removeEventListener('pointerdown', unlock);
    window.removeEventListener('keydown', unlock);
  };
}

type GamePoint = { x: number; y: number };
const game0_keys = new Set(['KeyR', 'ArrowUp', 'Space', 'Escape', 'F5']);

export function connectGame0(callback: DotNetCallback, surface: SVGSVGElement): void {
  detach?.();
  let connected = true;
  let pending: Promise<unknown> = Promise.resolve();
  let active_pointer: number | undefined;
  let pointer_buttons = 0;
  let last_point: GamePoint = { x: 0, y: 0 };
  const pressed_keys = new Set<string>();
  const previous_touch_action = surface.style.getPropertyValue('touch-action');
  const previous_touch_priority = surface.style.getPropertyPriority('touch-action');
  surface.style.setProperty('touch-action', 'none');

  const invoke = (method: string, ...args: unknown[]) => {
    pending = pending.then(() => {
      if (connected) return callback.invokeMethodAsync(method, ...args);
    }).catch(error => { console.warn('Could not deliver game input', error); });
  };
  let viewport_scale: number | undefined;
  const updateViewport = () => {
    if (!connected) return;
    const bounds = surface.getBoundingClientRect();
    const scale = Math.min(bounds.width / 1448, bounds.height / 1086);
    if (!Number.isFinite(scale) || scale <= 0 || scale === viewport_scale) return;
    viewport_scale = scale;
    invoke('ViewportChanged', scale);
  };
  const resize_observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(updateViewport);
  resize_observer?.observe(surface);
  updateViewport();
  const pointFor = (event: PointerEvent): GamePoint | undefined => {
    const matrix = surface.getScreenCTM();
    if (!matrix) return;
    const point = new DOMPoint(event.clientX, event.clientY).matrixTransform(matrix.inverse());
    if (!Number.isFinite(point.x) || !Number.isFinite(point.y)) return;
    return { x: point.x, y: point.y };
  };
  const pointerChanged = (phase: string, point: GamePoint, button: number) => {
    last_point = point;
    invoke('PointerChanged', phase, point.x, point.y, button);
  };
  const releaseCapture = (pointer_id: number) => {
    try {
      if (surface.hasPointerCapture(pointer_id)) surface.releasePointerCapture(pointer_id);
    } catch { /* Capture may already be gone after a canceled pointer. */ }
  };
  const finishPointer = (phase: 'up' | 'cancel', event?: PointerEvent) => {
    if (active_pointer === undefined) return;
    const pointer_id = active_pointer;
    active_pointer = undefined;
    pointer_buttons = 0;
    pointerChanged(phase, event && pointFor(event) || last_point, 0);
    releaseCapture(pointer_id);
  };
  const pointerdown = (event: PointerEvent) => {
    if (event.target instanceof Element && event.target.closest('[data-game0-control]')) return;
    if (active_pointer !== undefined && active_pointer !== event.pointerId) return;
    if (event.button !== 0 && event.button !== 2) return;
    const point = pointFor(event);
    if (!point) return;
    if (event.button === 2) {
      event.preventDefault();
      if (active_pointer !== undefined) pointer_buttons = event.buttons;
      pointerChanged('down', point, 2);
      return;
    }
    if (active_pointer !== undefined) return;
    try { surface.setPointerCapture(event.pointerId); }
    catch { return; }
    event.preventDefault();
    active_pointer = event.pointerId;
    pointer_buttons = event.buttons;
    surface.focus({ preventScroll: true });
    pointerChanged('down', point, 0);
  };
  const pointermove = (event: PointerEvent) => {
    if (active_pointer !== event.pointerId) return;
    event.preventDefault();
    const samples = event.getCoalescedEvents?.() ?? [];
    const last_sample = samples.at(-1);
    if (!last_sample || last_sample.clientX !== event.clientX || last_sample.clientY !== event.clientY) samples.push(event);
    for (const sample of samples) {
      const point = pointFor(sample);
      if (point) pointerChanged('move', point, 0);
    }
    // Mouse button chords emit pointermove while another button is held.
    if ((event.buttons & 2) !== (pointer_buttons & 2)) {
      pointerChanged(event.buttons & 2 ? 'down' : 'up', last_point, 2);
    }
    pointer_buttons = event.buttons;
    if (!(event.buttons & 1)) finishPointer('up', event);
  };
  const pointerup = (event: PointerEvent) => {
    if (active_pointer !== undefined && active_pointer !== event.pointerId) return;
    if (active_pointer === undefined) {
      if (event.button === 2) {
        const point = pointFor(event);
        if (point) pointerChanged('up', point, 2);
      }
      return;
    }
    event.preventDefault();
    if (event.button === 2) {
      pointerChanged('up', pointFor(event) || last_point, 2);
      pointer_buttons = event.buttons;
      if (event.buttons & 1) return;
    }
    finishPointer('up', event);
  };
  const pointercancel = (event: PointerEvent) => {
    if (active_pointer === event.pointerId) finishPointer('cancel');
  };
  const contextmenu = (event: MouseEvent) => { event.preventDefault(); };
  const keydown = (event: KeyboardEvent) => {
    if (ignoresKeyboard(event.target) || !game0_keys.has(event.code) || event.ctrlKey || event.metaKey || event.altKey) return;
    if (isButton(event.target) && event.code === 'Space') return;
    event.preventDefault();
    if (event.repeat || pressed_keys.has(event.code)) return;
    pressed_keys.add(event.code);
    invoke('KeyChanged', event.code, true);
  };
  const keyup = (event: KeyboardEvent) => {
    if (!pressed_keys.delete(event.code)) return;
    invoke('KeyChanged', event.code, false);
  };
  const pause = () => {
    finishPointer('cancel');
    for (const key of pressed_keys) invoke('KeyChanged', key, false);
    pressed_keys.clear();
    invoke('PauseForFocus');
  };
  const visibility = () => { if (document.hidden) pause(); };
  const unlock = () => { if (audio?.state === 'suspended') void audio.resume(); };
  surface.addEventListener('pointerdown', pointerdown);
  surface.addEventListener('pointermove', pointermove);
  surface.addEventListener('pointerup', pointerup);
  surface.addEventListener('pointercancel', pointercancel);
  surface.addEventListener('lostpointercapture', pointercancel);
  surface.addEventListener('contextmenu', contextmenu);
  window.addEventListener('keydown', keydown);
  window.addEventListener('keyup', keyup);
  window.addEventListener('blur', pause);
  document.addEventListener('visibilitychange', visibility);
  window.addEventListener('pointerdown', unlock);
  window.addEventListener('keydown', unlock);
  detach = () => {
    connected = false;
    resize_observer?.disconnect();
    if (active_pointer !== undefined) releaseCapture(active_pointer);
    active_pointer = undefined;
    surface.removeEventListener('pointerdown', pointerdown);
    surface.removeEventListener('pointermove', pointermove);
    surface.removeEventListener('pointerup', pointerup);
    surface.removeEventListener('pointercancel', pointercancel);
    surface.removeEventListener('lostpointercapture', pointercancel);
    surface.removeEventListener('contextmenu', contextmenu);
    window.removeEventListener('keydown', keydown);
    window.removeEventListener('keyup', keyup);
    window.removeEventListener('blur', pause);
    document.removeEventListener('visibilitychange', visibility);
    window.removeEventListener('pointerdown', unlock);
    window.removeEventListener('keydown', unlock);
    if (previous_touch_action) surface.style.setProperty('touch-action', previous_touch_action, previous_touch_priority);
    else surface.style.removeProperty('touch-action');
  };
}

export function disconnect(): void { detach?.(); detach = undefined; }
export function loadBest(): number {
  try { return Math.max(0, Number(localStorage.getItem('chroma_best')) || 0); } catch { return 0; }
}
export function saveBest(score: number): void {
  try { localStorage.setItem('chroma_best', String(score)); } catch { /* Saving is optional. */ }
}
export function tone(frequency: number, duration: number): void {
  audio ??= new AudioContext();
  if (audio.state === 'suspended') void audio.resume();
  const oscillator = audio.createOscillator();
  const gain = audio.createGain();
  oscillator.frequency.value = frequency;
  gain.gain.setValueAtTime(0.07, audio.currentTime);
  gain.gain.exponentialRampToValueAtTime(0.001, audio.currentTime + duration);
  oscillator.connect(gain).connect(audio.destination);
  oscillator.start();
  oscillator.stop(audio.currentTime + duration);
}

export function loadLanguage(): string {
  try { return localStorage.getItem('chroma_language') || navigator.language; }
  catch { return navigator.language; }
}
export async function setLanguage(language: string): Promise<void> {
  document.documentElement.lang = language;
  try { localStorage.setItem('chroma_language', language); } catch { /* Saving is optional. */ }
  try {
    const catalog = await loadCatalog();
    const strings = catalog[language] ?? catalog.en;
    for (const element of document.querySelectorAll<HTMLElement>('[data-i18n]')) {
      element.textContent = strings[element.dataset.i18n!];
    }
  } catch (error) { console.warn('Could not translate startup messages', error); }
}
type Catalog = Record<string, Record<string, string>>;
let catalog: Promise<Catalog> | undefined;
function loadCatalog(): Promise<Catalog> {
  return catalog ??= fetch('locales.json').then(response => {
    if (!response.ok) throw new Error('Translation catalog unavailable');
    return response.json() as Promise<Catalog>;
  });
}
export async function initializeLanguage(): Promise<void> {
  const prefix = loadLanguage().replaceAll('_', '-').split('-')[0].toLowerCase();
  await setLanguage(prefix === 'zh' || prefix === 'cn' ? 'zh-CN' : prefix === 'ja' ? 'ja' : 'en');
}

export function loadCompleted(): number {
  try { return Math.max(0, Math.trunc(Number(localStorage.getItem('chroma_completed')) || 0)) & 0x7fffffff; }
  catch { return 0; }
}
export function saveCompleted(completed: number): void {
  try { localStorage.setItem('chroma_completed', String(completed)); } catch { /* Saving is optional. */ }
}
