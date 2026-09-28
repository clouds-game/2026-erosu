import { connectGame0, disconnect, type DotNetCallback } from '../Interop/interop.js';

class TestElement extends EventTarget {
  parent: TestElement | undefined;
  content_editable = false;
  game0_control = false;

  constructor(readonly tag: string = 'div') { super(); }

  get isContentEditable(): boolean { return this.content_editable || !!this.parent?.isContentEditable; }

  closest(selector: string): TestElement | null {
    for (let element: TestElement | undefined = this; element; element = element.parent) {
      if (selector === 'button' && element.tag === 'button') return element;
      if (selector === '[data-game0-control]' && element.game0_control) return element;
      if (selector === 'input, textarea, select, [role="textbox"]' && ['input', 'textarea', 'select', 'textbox'].includes(element.tag)) return element;
    }
    return null;
  }
}

class TestStyle {
  value = '';
  priority = '';
  getPropertyValue(): string { return this.value; }
  getPropertyPriority(): string { return this.priority; }
  setProperty(_name: string, value: string, priority = ''): void { this.value = value; this.priority = priority; }
  removeProperty(): string { const previous = this.value; this.value = ''; this.priority = ''; return previous; }
}

type Matrix = { a: number; d: number; e: number; f: number };
class TestPoint {
  constructor(readonly x: number, readonly y: number) {}
  matrixTransform(matrix: Matrix): TestPoint { return new TestPoint(this.x * matrix.a + matrix.e, this.y * matrix.d + matrix.f); }
}

class TestSurface extends TestElement {
  readonly style = new TestStyle();
  readonly captured = new Set<number>();
  matrix: Matrix | undefined = { a: 1, d: 1, e: 0, f: 0 };
  viewport = { width: 0, height: 0 };

  getBoundingClientRect(): { width: number; height: number } { return this.viewport; }

  getScreenCTM(): (Matrix & { inverse: () => Matrix }) | null {
    const matrix = this.matrix;
    return matrix ? { ...matrix, inverse: () => ({ a: 1 / matrix.a, d: 1 / matrix.d, e: -matrix.e / matrix.a, f: -matrix.f / matrix.d }) } : null;
  }
  focus(): void { test_document.activeElement = this; }
  setPointerCapture(pointer_id: number): void { this.captured.add(pointer_id); }
  hasPointerCapture(pointer_id: number): boolean { return this.captured.has(pointer_id); }
  releasePointerCapture(pointer_id: number): void {
    this.captured.delete(pointer_id);
    this.dispatchEvent(pointer('lostpointercapture', { pointer_id }));
  }
}

class TestResizeObserver {
  static latest: TestResizeObserver | undefined;
  observed: TestSurface | undefined;
  disconnected = false;
  constructor(readonly callback: () => void) { TestResizeObserver.latest = this; }
  observe(surface: TestSurface): void { this.observed = surface; }
  disconnect(): void { this.disconnected = true; this.observed = undefined; }
  emit(): void { this.callback(); }
}

const test_window = new EventTarget();
const test_document = Object.assign(new EventTarget(), { hidden: false, activeElement: undefined as TestElement | undefined });
Object.assign(globalThis, {
  Element: TestElement, HTMLElement: TestElement, DOMPoint: TestPoint, ResizeObserver: TestResizeObserver,
  window: test_window, document: test_document,
});

type PointerOptions = {
  pointer_id?: number; x?: number; y?: number; button?: number; buttons?: number;
  target?: TestElement; samples?: Event[];
};
function pointer(type: string, options: PointerOptions = {}): Event {
  const event = new Event(type, { cancelable: true });
  Object.assign(event, {
    pointerId: options.pointer_id ?? 1, clientX: options.x ?? 0, clientY: options.y ?? 0,
    button: options.button ?? 0, buttons: options.buttons ?? 1,
    getCoalescedEvents: () => options.samples ?? [],
  });
  if (options.target) Object.defineProperty(event, 'target', { value: options.target });
  return event;
}
function key(type: string, code: string, options: { target?: TestElement; repeat?: boolean; ctrl?: boolean } = {}): Event {
  const event = new Event(type, { cancelable: true });
  Object.assign(event, { code, repeat: !!options.repeat, ctrlKey: !!options.ctrl, metaKey: false, altKey: false });
  const target = options.target ?? test_document.activeElement;
  if (target) Object.defineProperty(event, 'target', { value: target });
  return event;
}
function equal(actual: unknown, expected: unknown): void {
  if (JSON.stringify(actual) !== JSON.stringify(expected)) throw new Error(`Expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
}
function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}
function recorder(): { callback: DotNetCallback; calls: unknown[][] } {
  const calls: unknown[][] = [];
  return { calls, callback: { async invokeMethodAsync(method, ...args) { calls.push([method, ...args]); } } };
}
function connect(surface: TestSurface, callback: DotNetCallback): void { connectGame0(callback, surface as unknown as SVGSVGElement); }
function flush(): Promise<void> { return new Promise(resolve => setTimeout(resolve, 0)); }
async function test(name: string, body: () => Promise<void>): Promise<void> {
  try { await body(); console.log(`PASS ${name}`); }
  finally { disconnect(); test_document.hidden = false; test_document.activeElement = undefined; }
}

await test('viewBox coordinates track letterboxing and resize, with one captured pointer', async () => {
  const surface = new TestSurface();
  const { callback, calls } = recorder();
  surface.matrix = { a: 0.5, d: 0.5, e: 100, f: 40 };
  connect(surface, callback);
  surface.dispatchEvent(pointer('pointerdown', { x: 300, y: 240 }));
  surface.dispatchEvent(pointer('pointerdown', { pointer_id: 2, x: 400, y: 300 }));
  assert(surface.hasPointerCapture(1) && !surface.hasPointerCapture(2), 'Only the first pointer should be captured');
  surface.matrix = { a: 0.25, d: 0.25, e: 20, f: 30 };
  surface.dispatchEvent(pointer('pointermove', { x: 145, y: 180 }));
  surface.dispatchEvent(pointer('pointerup', { x: 170, y: 205, buttons: 0 }));
  await flush();
  equal(calls, [
    ['PointerChanged', 'down', 400, 400, 0], ['PointerChanged', 'move', 500, 600, 0], ['PointerChanged', 'up', 600, 700, 0],
  ]);
  assert(!surface.hasPointerCapture(1), 'Release should clear pointer capture');
});

await test('coalesced samples and terminal events wait for earlier asynchronous callbacks', async () => {
  const surface = new TestSurface();
  const calls: unknown[][] = [];
  let release: (() => void) | undefined;
  const first = new Promise<void>(resolve => { release = resolve; });
  connect(surface, { async invokeMethodAsync(method, ...args) {
    calls.push([method, ...args]);
    if (calls.length === 1) await first;
  } });
  surface.dispatchEvent(pointer('pointerdown', { x: 10, y: 20 }));
  surface.dispatchEvent(pointer('pointermove', { x: 40, y: 50, samples: [pointer('pointermove', { x: 20, y: 30 }), pointer('pointermove', { x: 30, y: 40 })] }));
  surface.dispatchEvent(pointer('pointerup', { x: 50, y: 60, buttons: 0 }));
  await flush();
  equal(calls, [['PointerChanged', 'down', 10, 20, 0]]);
  release!();
  await flush();
  equal(calls, [
    ['PointerChanged', 'down', 10, 20, 0], ['PointerChanged', 'move', 20, 30, 0],
    ['PointerChanged', 'move', 30, 40, 0], ['PointerChanged', 'move', 40, 50, 0], ['PointerChanged', 'up', 50, 60, 0],
  ]);
});

await test('right button chords rotate during drag and left release finishes while right stays held', async () => {
  const surface = new TestSurface();
  const { callback, calls } = recorder();
  connect(surface, callback);
  surface.dispatchEvent(pointer('pointerdown'));
  surface.dispatchEvent(pointer('pointermove', { x: 10, y: 20, button: 2, buttons: 3 }));
  surface.dispatchEvent(pointer('pointermove', { x: 30, y: 40, button: 0, buttons: 2 }));
  surface.dispatchEvent(pointer('pointerup', { x: 30, y: 40, button: 2, buttons: 0 }));
  await flush();
  equal(calls.filter(call => call[1] !== 'move'), [
    ['PointerChanged', 'down', 0, 0, 0], ['PointerChanged', 'down', 10, 20, 2],
    ['PointerChanged', 'up', 30, 40, 0], ['PointerChanged', 'up', 30, 40, 2],
  ]);
  assert(!surface.hasPointerCapture(1), 'Left release must end drag without waiting for right release');
});

await test('pointer cancellation and lost capture cancel once without committing a drop', async () => {
  const surface = new TestSurface();
  const { callback, calls } = recorder();
  connect(surface, callback);
  surface.dispatchEvent(pointer('pointerdown', { pointer_id: 7, x: 14, y: 18 }));
  surface.dispatchEvent(pointer('pointercancel', { pointer_id: 8 }));
  surface.dispatchEvent(pointer('pointercancel', { pointer_id: 7 }));
  surface.dispatchEvent(pointer('lostpointercapture', { pointer_id: 7 }));
  surface.dispatchEvent(pointer('pointerdown', { pointer_id: 8, x: 21, y: 22 }));
  surface.releasePointerCapture(8);
  await flush();
  equal(calls, [
    ['PointerChanged', 'down', 14, 18, 0], ['PointerChanged', 'cancel', 14, 18, 0],
    ['PointerChanged', 'down', 21, 22, 0], ['PointerChanged', 'cancel', 21, 22, 0],
  ]);
});

await test('blur and hidden documents cancel dragging and release keys before pausing', async () => {
  const surface = new TestSurface();
  const { callback, calls } = recorder();
  connect(surface, callback);
  test_window.dispatchEvent(key('keydown', 'KeyR'));
  surface.dispatchEvent(pointer('pointerdown', { x: 1, y: 2 }));
  test_window.dispatchEvent(new Event('blur'));
  surface.dispatchEvent(pointer('pointerdown', { x: 3, y: 4 }));
  test_document.hidden = true;
  test_document.dispatchEvent(new Event('visibilitychange'));
  await flush();
  equal(calls, [
    ['KeyChanged', 'KeyR', true], ['PointerChanged', 'down', 1, 2, 0], ['PointerChanged', 'cancel', 1, 2, 0],
    ['KeyChanged', 'KeyR', false], ['PauseForFocus'],
    ['PointerChanged', 'down', 3, 4, 0], ['PointerChanged', 'cancel', 3, 4, 0], ['PauseForFocus'],
  ]);
});

await test('game keys ignore typing, select controls, native button activation and modifiers', async () => {
  const surface = new TestSurface();
  const { callback, calls } = recorder();
  connect(surface, callback);
  for (const tag of ['input', 'textarea', 'select', 'textbox']) {
    const event = key('keydown', 'Space', { target: new TestElement(tag) });
    test_window.dispatchEvent(event);
    assert(!event.defaultPrevented, `${tag} should retain normal keyboard behavior`);
  }
  const editable = new TestElement();
  editable.content_editable = true;
  const child = new TestElement('span');
  child.parent = editable;
  test_window.dispatchEvent(key('keydown', 'ArrowUp', { target: child }));
  const button = new TestElement('button');
  const label = new TestElement('span');
  label.parent = button;
  for (const code of ['Enter', 'Space']) {
    const event = key('keydown', code, { target: label });
    test_window.dispatchEvent(event);
    assert(!event.defaultPrevented, 'Native button activation must remain available');
  }
  test_window.dispatchEvent(key('keydown', 'ArrowUp', { ctrl: true }));
  for (const code of ['KeyR', 'ArrowUp', 'Space', 'Escape', 'F5']) {
    const event = key('keydown', code);
    test_window.dispatchEvent(event);
    assert(event.defaultPrevented, `${code} should be handled by the game`);
    test_window.dispatchEvent(key('keydown', code, { repeat: true }));
    test_window.dispatchEvent(key('keyup', code));
  }
  await flush();
  equal(calls, ['KeyR', 'ArrowUp', 'Space', 'Escape', 'F5'].flatMap(code => [['KeyChanged', code, true], ['KeyChanged', code, false]]));
});

await test('disconnect removes listeners, drops queued events, releases capture and restores touch action', async () => {
  const surface = new TestSurface();
  surface.style.setProperty('touch-action', 'pan-y', 'important');
  const { callback, calls } = recorder();
  connect(surface, callback);
  equal([surface.style.value, surface.style.priority], ['none', '']);
  const contextmenu = new Event('contextmenu', { cancelable: true });
  surface.dispatchEvent(contextmenu);
  assert(contextmenu.defaultPrevented, 'Game surface context menus should be disabled');
  surface.dispatchEvent(pointer('pointerdown'));
  await flush();
  surface.dispatchEvent(pointer('pointermove', { x: 10, y: 20 }));
  disconnect();
  surface.dispatchEvent(pointer('pointerdown', { pointer_id: 2 }));
  test_window.dispatchEvent(new Event('blur'));
  const after_disconnect = new Event('contextmenu', { cancelable: true });
  surface.dispatchEvent(after_disconnect);
  await flush();
  equal(calls, [['PointerChanged', 'down', 0, 0, 0]]);
  equal([surface.style.value, surface.style.priority], ['pan-y', 'important']);
  assert(surface.captured.size === 0, 'Disconnect should release active capture');
  assert(!after_disconnect.defaultPrevented, 'Context menu listener should be removed');
});

await test('native Game0 controls and missing screen transforms never start a drag', async () => {
  const surface = new TestSurface();
  const { callback, calls } = recorder();
  connect(surface, callback);
  const foreign_object = new TestElement();
  foreign_object.game0_control = true;
  const button = new TestElement('button');
  button.parent = foreign_object;
  const down = pointer('pointerdown', { target: button });
  surface.dispatchEvent(down);
  assert(!down.defaultPrevented, 'Native Game0 control clicks must remain available');
  surface.matrix = undefined;
  surface.dispatchEvent(pointer('pointerdown'));
  await flush();
  equal(calls, []);
  assert(surface.captured.size === 0, 'Invalid transforms cannot capture a pointer');
});

await test('starting a drag transfers focus from controls so Space rotates the held piece', async () => {
  const surface = new TestSurface();
  const { callback, calls } = recorder();
  const button = new TestElement('button');
  test_document.activeElement = button;
  connect(surface, callback);
  const button_space = key('keydown', 'Space');
  test_window.dispatchEvent(button_space);
  assert(!button_space.defaultPrevented, 'Focused buttons should retain Space activation');
  surface.dispatchEvent(pointer('pointerdown', { x: 1, y: 2 }));
  assert(test_document.activeElement === surface, 'Primary pointerdown should focus the SVG game surface');
  const rotate = key('keydown', 'Space');
  test_window.dispatchEvent(rotate);
  await flush();
  assert(rotate.defaultPrevented, 'Space should rotate after the drag takes focus');
  equal(calls, [['PointerChanged', 'down', 1, 2, 0], ['KeyChanged', 'Space', true]]);
});

await test('viewport observer reports initial and changed scale, then stops on disconnect', async () => {
  const surface = new TestSurface();
  surface.viewport = { width: 1448, height: 1086 };
  const { callback, calls } = recorder();
  connect(surface, callback);
  const observer = TestResizeObserver.latest!;
  assert(observer.observed === surface, 'ResizeObserver should watch the SVG surface');
  surface.viewport = { width: 724, height: 400 };
  observer.emit();
  observer.emit();
  surface.viewport = { width: 0, height: 0 };
  observer.emit();
  surface.dispatchEvent(pointer('pointerdown', { x: 10, y: 20 }));
  await flush();
  equal(calls, [['ViewportChanged', 1], ['ViewportChanged', 400 / 1086], ['PointerChanged', 'down', 10, 20, 0]]);
  disconnect();
  assert(observer.disconnected, 'Disconnect should dispose the viewport observer');
  surface.viewport = { width: 2896, height: 2172 };
  observer.emit();
  await flush();
  equal(calls.length, 3);
  Object.assign(globalThis, { ResizeObserver: undefined });
  try {
    connect(surface, callback);
    await flush();
    equal(calls.at(-1), ['ViewportChanged', 2]);
  } finally { Object.assign(globalThis, { ResizeObserver: TestResizeObserver }); }
});

console.log('Game0 input adapter: 10 tests passed');
