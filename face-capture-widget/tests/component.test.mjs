import test from 'node:test';
import assert from 'node:assert/strict';
import { setTimeout as delay } from 'node:timers/promises';
import { Window } from 'happy-dom';
import { startAgent } from './agent-fixture.mjs';

const window = new Window({ url: 'http://127.0.0.1/' });
for (const key of ['window', 'document', 'Element', 'HTMLElement', 'SVGElement', 'Node']) {
  globalThis[key] = key === 'window' ? window : window[key];
}
const { createApp, h, ref, nextTick } = await import('vue');
// Exercise the exact build shipped in the tarball, with the real SDK and WebSocket.
const { FaceCaptureDialog } = await import('../dist/face-capture-vue.js');
async function until(check) {
  const deadline = Date.now() + 3000;
  while (!check()) {
    if (Date.now() > deadline) assert.fail('Component did not reach expected state');
    await delay(10);
  }
  await nextTick();
}
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
