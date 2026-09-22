type DotNetCallback = { invokeMethodAsync: (method: string, ...args: unknown[]) => Promise<unknown> };
const game_keys = new Set(['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'KeyA', 'KeyD', 'KeyW', 'KeyS', 'Space', 'KeyP', 'Escape', 'KeyR', 'F2', 'KeyM', 'Enter']);
let detach: (() => void) | undefined;
let audio: AudioContext | undefined;

export function connect(callback: DotNetCallback): void {
  detach?.();
  const keydown = (event: KeyboardEvent) => {
    if (!game_keys.has(event.code) || event.ctrlKey || event.metaKey || event.altKey) return;
    if (event.target instanceof HTMLButtonElement && ['Enter', 'Space'].includes(event.code)) return;
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
