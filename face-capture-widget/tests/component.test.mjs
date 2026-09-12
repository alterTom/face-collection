import test from 'node:test';
import assert from 'node:assert/strict';
import { setTimeout as delay } from 'node:timers/promises';
import { Window } from 'happy-dom';
import { startAgent } from './agent-fixture.mjs';

const window = new Window({ url: 'http://127.0.0.1/' });
for (const key of ['window', 'document', 'Element', 'HTMLElement', 'SVGElement', 'Node']) {
  globalThis[key] = key === 'window' ? window : window[key];
}
const { createApp, h, ref, nextTick, onMounted } = await import('vue');
// Exercise the exact build shipped in the tarball, with the real SDK and WebSocket.
const { FaceCaptureDialog, FaceCapture } = await import('../dist/face-capture-vue.js');
async function until(check) {
  const deadline = Date.now() + 3000;
  while (!check()) {
    if (Date.now() > deadline) assert.fail('Component did not reach expected state');
    await delay(10);
  }
  await nextTick();
}

test('embedded capture follows active without owning the host page or buttons', async t => {
  assert.ok(FaceCapture, 'package must export FaceCapture');
  const agent = await startAgent({ captureDelay: 100 }); t.after(() => agent.close());
  const active = ref(false), capture = ref(null), photos = [], states = [], countdowns = [];
  const container = document.createElement('div'); document.body.append(container);
  const app = createApp({ setup: () => () => h(FaceCapture, { ref: capture, active: active.value,
    serviceUrl: agent.url, onSuccess: photo => photos.push(photo), onStateChange: s => states.push(s),
    onCountdown: value => countdowns.push(value),
  }) });
  app.mount(container); t.after(() => { app.unmount(); container.remove(); });
  await nextTick(); await delay(30);
  assert.equal(agent.connections, 0);
  assert.equal(container.querySelector('button, dialog'), null);
  active.value = true;
  await until(() => photos.length === 1);
  assert.equal(active.value, true); // host decides whether to close
  assert.equal(photos[0].blob.type, 'image/jpeg');
  assert.ok(states.some(s => s.phase === 'capturing'));
  assert.ok(countdowns.includes(60)); assert.equal(countdowns.at(-1), null);
  await until(() => agent.connections === 0);
  await delay(150); assert.equal(photos.length, 1);
  active.value = false; await nextTick(); active.value = true;
  await until(() => photos.length === 2);
});

test('embedded active=false cancels preview without unmounting and supports retake', async t => {
  assert.ok(FaceCapture);
  const agent = await startAgent({ captureDelay: 10_000 }); t.after(() => agent.close());
  const active = ref(true), capture = ref(null), results = [];
  const container = document.createElement('div'); document.body.append(container);
  const app = createApp({ setup: () => () => h(FaceCapture, { ref: capture, active: active.value,
    serviceUrl: agent.url, onResult: value => results.push(value) }) });
  app.mount(container); t.after(() => { app.unmount(); container.remove(); });
  await until(() => container.querySelector('img')?.hasAttribute('src'));
  await capture.value.retake();
  assert.ok(agent.commands.some(c => c.type === 'capture.rearm'));
  active.value = false;
  await until(() => agent.connections === 0);
  assert.equal(results[0].status, 'cancelled');
  assert.equal(container.querySelector('img').hasAttribute('src'), false);
});
function mount(props = {}) {
  const visible = ref(true), results = [], openAtResult = [];
  const container = document.createElement('div'); document.body.append(container);
  const app = createApp({ setup() { return () => h(FaceCaptureDialog, { ...props, modelValue: visible.value,
    'onUpdate:modelValue': value => { visible.value = value; },
    onResult: result => { openAtResult.push(!!document.querySelector('dialog[open]')); results.push(result); },
  }); } });
  app.mount(container);
  return { visible, results, openAtResult, unmount() { app.unmount(); container.remove(); } };
}

test('closing parent immediately after modal opens cancels pending startup', async t => {
  const agent = await startAgent(); t.after(() => agent.close());
  const original = window.HTMLDialogElement.prototype.showModal;
  let c;
  window.HTMLDialogElement.prototype.showModal = function () {
    original.call(this);
    queueMicrotask(() => { c.visible.value = false; });
  };
  t.after(() => { window.HTMLDialogElement.prototype.showModal = original; });
  c = mount({ serviceUrl: agent.url }); t.after(() => c.unmount());
  await delay(100);
  assert.equal(document.querySelector('dialog').open, false);
  assert.equal(agent.commands.length, 0);
  assert.deepEqual(c.results, [{ status: 'cancelled' }]);
});

test('core cancel during mount prevents pending activation from connecting', async t => {
  const agent = await startAgent(); t.after(() => agent.close());
  const capture = ref(null), results = [];
  const container = document.createElement('div'); document.body.append(container);
  const app = createApp({ setup() {
    onMounted(() => capture.value.cancel());
    return () => h(FaceCapture, { ref: capture, active: true, serviceUrl: agent.url, onResult: r => results.push(r) });
  } });
  app.mount(container); t.after(() => { app.unmount(); container.remove(); });
  await delay(150);
  assert.equal(agent.commands.length, 0);
  assert.deepEqual(results, [{ status: 'cancelled' }]);
});

test('real Agent auto event closes modal before returning Blob and releases WebSocket', async t => {
  const agent = await startAgent({ captureDelay: 200 }); t.after(() => agent.close());
  const c = mount({ serviceUrl: agent.url }); t.after(() => c.unmount());
  await until(() => document.querySelector('.fcw-preview')?.hasAttribute('src'));
  assert.equal(document.querySelector('[role="timer"]'), null);
  await until(() => c.results.length === 1);
  assert.equal(c.results[0].status, 'success');
  assert.equal(c.results[0].photo.blob.type, 'image/jpeg');
  assert.equal(c.results[0].photo.width, 640);
  assert.equal(c.visible.value, false);
  assert.deepEqual(c.openAtResult, [false]);
  await until(() => agent.connections === 0);
  assert.equal(document.querySelector('.fcw-preview').hasAttribute('src'), false);
});

test('timeout closes modal and returns a single connection_failed result', async t => {
  const agent = await startAgent(); const url = agent.url; await agent.close();
  const c = mount({ serviceUrl: url, connectionTimeoutMs: 60 }); t.after(() => c.unmount());
  await until(() => c.results.length);
  assert.equal(c.results[0].status, 'connection_failed');
  assert.equal(c.results[0].code, 'AGENT_CONNECTION_TIMEOUT');
  assert.deepEqual(c.openAtResult, [false]);
  await delay(80); assert.equal(c.results.length, 1);
});

test('all cancellation paths return once and reopening starts a clean session', async t => {
  const agent = await startAgent({ captureDelay: 10_000 }); t.after(() => agent.close());
  const c = mount({ serviceUrl: agent.url }); t.after(() => c.unmount());
  for (const action of ['exit', 'close', 'escape', 'parent', 'pagehide']) {
    c.visible.value = true;
    await until(() => document.querySelector('.fcw-preview')?.hasAttribute('src'));
    const count = c.results.length;
    if (action === 'exit') document.querySelector('.fcw-exit').click();
    if (action === 'close') document.querySelector('.fcw-close').click();
    if (action === 'escape') document.querySelector('dialog').dispatchEvent(new window.Event('cancel', { cancelable: true }));
    if (action === 'parent') c.visible.value = false;
    if (action === 'pagehide') window.dispatchEvent(new window.Event('pagehide'));
    await until(() => c.results.length === count + 1);
    assert.equal(c.results.at(-1).status, 'cancelled');
    assert.equal(c.visible.value, false);
    await until(() => agent.connections === 0);
  }
  assert.equal(c.results.length, 5);
});

test('unmount releases the active preview and reports cancellation', async t => {
  const agent = await startAgent({ captureDelay: 10_000 }); t.after(() => agent.close());
  const c = mount({ serviceUrl: agent.url });
  await until(() => document.querySelector('.fcw-preview')?.hasAttribute('src'));
  c.unmount();
  assert.equal(c.results[0].status, 'cancelled');
  await until(() => agent.connections === 0);
  assert.equal(document.querySelector('dialog'), null);
});

test('camera error stays visible; retake recovers and returns a photo', async t => {
  const agent = await startAgent({ busyOnce: true }); t.after(() => agent.close());
  const c = mount({ serviceUrl: agent.url }); t.after(() => c.unmount());
  await until(() => document.querySelector('.fcw-status--error'));
  assert.equal(c.results.length, 0);
  assert.equal(document.querySelector('dialog').open, true);
  assert.ok(!document.body.textContent.includes('internal details'));
  document.querySelector('.fcw-retake').click();
  await until(() => c.results.length);
  assert.equal(c.results[0].status, 'success');
});

test('retake resets the real Agent round and still returns only one photo', async t => {
  const agent = await startAgent({ captureDelay: 200 }); t.after(() => agent.close());
  const c = mount({ serviceUrl: agent.url }); t.after(() => c.unmount());
  await until(() => document.querySelector('.fcw-retake')?.disabled === false);
  document.querySelector('.fcw-retake').click();
  await until(() => c.results.length);
  assert.ok(agent.commands.some(command => command.type === 'capture.rearm'));
  assert.equal(c.results[0].status, 'success');
  await delay(250); assert.equal(c.results.length, 1);
});
