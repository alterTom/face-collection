import test from 'node:test';
import assert from 'node:assert/strict';
import { createCaptureSession } from '../src/session.js';

const flush = async () => { for (let i = 0; i < 20; i++) await Promise.resolve(); };
function setup(overrides = {}, options = {}) {
  let time = 0, id = 0;
  const timers = new Map(), results = [], states = [], clients = [];
  const clock = {
    now: () => time,
    setTimeout(fn, ms) { const key = ++id; timers.set(key, { fn, at: time + ms }); return key; },
    clearTimeout(key) { timers.delete(key); },
  };
  const createClient = (url, closed) => {
    const listeners = new Map();
    const client = {
      disconnected: false, opens: 0, rearms: 0,
      async connect() {}, async listDevices() { return [{ id: '0' }]; },
      async open(value) { client.opens++; client.options = value; },
      async startPreview() {}, async rearm() { client.rearms++; },
      disconnect() { client.disconnected = true; },
      on(type, cb) { listeners.set(type, cb); return () => listeners.delete(type); },
      event(type, data) { listeners.get(type)?.(data); }, closed,
      ...overrides,
    };
    clients.push(client); return client;
  };
  const session = createCaptureSession(options, { createClient, clock,
    onState: s => states.push(s), onResult: r => results.push(r) });
  const advance = async ms => {
    const target = time + ms;
    for (;;) {
      const next = [...timers].filter(([, t]) => t.at <= target).sort((a, b) => a[1].at - b[1].at)[0];
      if (!next) break;
      time = next[1].at; timers.delete(next[0]); next[1].fn(); await flush();
    }
    time = target; await flush();
  };
  return { session, clients, results, states, timers, advance, clock };
}

test('failed attempts share one 60 second deadline and stop after timeout', async () => {
  const f = setup({ async connect() { throw new Error('private data'); } });
  f.session.start({}); await flush();
  await f.advance(59_000);
  assert.equal(f.results.length, 0);
  assert.equal(f.states.at(-1).remainingSeconds, 1);
  assert.ok(f.clients.length > 1);
  await f.advance(1000);
  assert.equal(f.results[0].status, 'connection_failed');
  assert.equal(f.results[0].code, 'AGENT_CONNECTION_TIMEOUT');
  assert.equal(f.timers.size, 0);
  const attempts = f.clients.length;
  await f.advance(60_000);
  assert.equal(f.clients.length, attempts);
  assert.ok(f.clients.every(c => c.disconnected));
  assert.ok(!JSON.stringify(f.states).includes('private data'));
});

test('connection cancels deadline; auto capture returns one photo and frees resources', async () => {
  const f = setup(); f.session.start({}); await flush();
  assert.equal(f.states.at(-1).phase, 'capturing');
  assert.equal(f.clients[0].options.captureMode, 'auto');
  await f.advance(120_000); assert.equal(f.results.length, 0);
  const photo = { base64: '/9j/2Q==', width: 1, height: 1, mimeType: 'image/jpeg', size: 4 };
  f.clients[0].event('auto.capture', photo);
  f.clients[0].event('auto.capture', photo);
  assert.equal(f.results.length, 1);
  assert.equal(f.results[0].photo.base64, photo.base64);
  assert.equal(f.results[0].photo.blob.type, 'image/jpeg');
  assert.equal(f.results[0].photo.blob.size, 4);
  assert.ok(f.clients[0].disconnected);
  assert.equal(f.timers.size, 0);
});

test('cancellation invalidates a pending connection and late events', async () => {
  let resolve;
  const f = setup({ connect: () => new Promise(r => { resolve = r; }) });
  f.session.start({}); f.session.cancel(); resolve(); await flush();
  assert.deepEqual(f.results, [{ status: 'cancelled' }]);
  assert.equal(f.clients[0].opens, 0);
  assert.equal(f.timers.size, 0);
});

test('timeout rejects connection success delivered after deadline', async () => {
  let resolve;
  const f = setup({ connect: () => new Promise(r => { resolve = r; }) });
  f.session.start({}); await f.advance(60_000); resolve(); await flush();
  assert.equal(f.results.length, 1);
  assert.equal(f.results[0].status, 'connection_failed');
  assert.equal(f.clients[0].opens, 0);
});

test('retake while connecting does not restart or extend deadline', async () => {
  const f = setup({ async connect() { throw new Error(); } });
  f.session.start({}); await f.advance(30_000); await f.session.retake();
  await f.advance(30_000);
  assert.equal(f.results[0].status, 'connection_failed');
});

test('device failure is recoverable and retake starts a fresh session', async () => {
  let fail = true;
  const f = setup({ async listDevices() { return fail ? [] : [{ id: '0' }]; } });
  f.session.start({}); await flush();
  assert.equal(f.states.at(-1).phase, 'error');
  assert.equal(f.results.length, 0);
  fail = false; await f.session.retake(); await flush();
  assert.equal(f.states.at(-1).phase, 'capturing');
  f.session.cancel();
});

test('retake rearms detection and duplicate clicks are ignored', async () => {
  let resolve;
  const f = setup({ rearm() { this.rearms++; return new Promise(r => { resolve = r; }); } });
  f.session.start({}); await flush();
  const pending = f.session.retake(); f.session.retake();
  assert.equal(f.clients[0].rearms, 1);
  resolve(); await pending;
  assert.equal(f.states.at(-1).phase, 'capturing'); f.session.cancel();
});

test('unexpected disconnection clears preview and allows recovery', async () => {
  const f = setup(); f.session.start({}); await flush();
  f.clients[0].closed();
  assert.equal(f.states.at(-1).phase, 'error');
  assert.ok(f.clients[0].disconnected);
  await f.session.retake(); await flush();
  assert.equal(f.states.at(-1).phase, 'capturing'); f.session.cancel();
});

test('cancel while camera is opening prevents late preview startup', async () => {
  let resolve, previews = 0;
  const f = setup({ open: () => new Promise(r => { resolve = r; }), async startPreview() { previews++; } });
  f.session.start({}); await flush(); f.session.cancel(); resolve(); await flush();
  assert.equal(previews, 0); assert.equal(f.results.length, 1);
});

test('invalid options return configuration error without opening a socket', async () => {
  for (const options of [{ serviceUrl: 'ws://example.com/face' }, { stableDurationMs: 100 }, { connectionTimeoutMs: NaN }]) {
    const f = setup({}, options); f.session.start({}); await flush();
    assert.equal(f.results[0].status, 'error');
    assert.equal(f.results[0].code, 'INVALID_OPTIONS');
    assert.equal(f.clients.length, 0);
  }
});
