type DotNetCallback = { invokeMethodAsync: (method: string, ...args: unknown[]) => Promise<unknown> };
const game_keys = new Set(['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'KeyA', 'KeyD', 'KeyW', 'KeyS', 'Space', 'KeyP', 'Escape', 'KeyR', 'F2', 'KeyM', 'Enter']);
let detach: (() => void) | undefined;
let audio: AudioContext | undefined;

export function connect(callback: DotNetCallback): void {
  detach?.();
  const keydown = (event: KeyboardEvent) => {
    if (event.target instanceof HTMLSelectElement || event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) return;
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
export function loadRecord(key: string, legacyKey?: string): number {
  try {
    const value = localStorage.getItem(`chroma_record_${key}`) ?? (legacyKey ? localStorage.getItem(legacyKey) : null);
    return Math.max(0, Math.trunc(Number(value) || 0));
  } catch { return 0; }
}
export function saveRecord(key: string, value: number): void {
  try { localStorage.setItem(`chroma_record_${key}`, String(value)); } catch { /* Saving is optional. */ }
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

export function loadCompletedIds(): number[] {
  try {
    const saved = localStorage.getItem('chroma_completed_levels');
    if (saved) return (JSON.parse(saved) as unknown[]).filter(value => Number.isInteger(value) && Number(value) > 0).map(Number);
    const legacy = Math.max(0, Math.trunc(Number(localStorage.getItem('chroma_completed')) || 0)) & 0x7fffffff;
    return Array.from({ length: 31 }, (_, index) => index + 1).filter(number => (legacy & (1 << (number - 1))) !== 0);
  } catch { return []; }
}
export function saveCompletedIds(completed: number[]): void {
  try {
    const normalized = [...new Set(completed.filter(number => Number.isInteger(number) && number > 0))].sort((a, b) => a - b);
    localStorage.setItem('chroma_completed_levels', JSON.stringify(normalized));
    const legacy = normalized.filter(number => number <= 31).reduce((mask, number) => mask | (1 << (number - 1)), 0);
    localStorage.setItem('chroma_completed', String(legacy));
  } catch { /* Saving is optional. */ }
}
