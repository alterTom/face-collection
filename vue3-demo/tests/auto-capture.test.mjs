import test from 'node:test';
import assert from 'node:assert/strict';
import { createCaptureController } from '../src/capture-controller.js';

const face = { x: 0.3, y: 0.2, width: 0.3, height: 0.4 };
const flush = () => new Promise(resolve => setImmediate(resolve));
async function setup(detectorOverrides = {}) {
  let captures = 0, time = 0, closes = 0;
  const image = new EventTarget();
  image.removeAttribute = () => {};
  const detector = Object.defineProperties({ ready: Promise.resolve(), async detect() { return [face]; }, close() { closes++; } }, Object.getOwnPropertyDescriptors(detectorOverrides));
  const client = {
    async connect() {}, async listDevices() { return [{ id: '0' }]; },
    async open() { return { width: 640, height: 480 }; }, async startPreview() {},
    async close() {}, disconnect() {},
    async capture() { captures++; return { width: 640, height: 480, size: 1, base64: 'AA==' }; },
  };
  const c = createCaptureController({ createClient: () => client, createDetector: () => detector, now: () => time });
  await c.connect();
  await c.open(image);
  async function frame(t) { time = t; image.dispatchEvent(new Event('load')); await flush(); }
  return { c, frame, captures: () => captures, closes: () => closes };
}
test('manual stays default; auto captures once and rearm allows another stable capture', async () => {
  const { c, frame, captures } = await setup();
  assert.equal(c.state.captureMode, 'manual');
  for (let t = 0; t <= 2000; t += 200) await frame(t);
  assert.equal(captures(), 0);
  await c.setCaptureMode('auto');
  for (let t = 2200; t <= 5000; t += 200) await frame(t);
  assert.equal(captures(), 1);
  assert.equal(c.state.autoStatus, '拍照完成');
  await c.retake();
  for (let t = 5200; t <= 7000; t += 200) await frame(t);
  assert.equal(captures(), 2);
  c.disconnect();
});
test('late detection cannot capture after switching mode, closing or disconnecting', async () => {
  for (const stop of [c => c.setCaptureMode('manual'), c => c.close(), c => c.disconnect()]) {
    let finish;
    let delayed = false;
    const { c, frame, captures } = await setup({ detect: () => delayed ? new Promise(resolve => { finish = resolve; }) : Promise.resolve([face]) });
    await c.setCaptureMode('auto');
    for (let t = 0; t <= 1400; t += 200) await frame(t);
    delayed = true;
    await frame(1600);
    await stop(c);
    finish([face]);
    await flush();
    assert.equal(captures(), 0);
    c.disconnect();
  }
});
test('model failure leaves manual capture usable with an actionable message', async () => {
  const { c, captures } = await setup({ get ready() { return Promise.reject(new Error('private failure')); } });
  await c.setCaptureMode('auto');
  assert.match(c.state.autoStatus, /手动/);
  await c.setCaptureMode('manual');
  await c.capture();
  assert.equal(captures(), 1);
  c.disconnect();
});

test('disconnect while loading the model releases it and ignores late initialization', async () => {
  let loaded;
  const { c, frame, captures, closes } = await setup({ ready: new Promise(resolve => { loaded = resolve; }) });
  const loading = c.setCaptureMode('auto');
  c.disconnect();
  loaded();
  await loading;
  await frame(2000);
  assert.equal(captures(), 0);
  assert.equal(closes(), 1);
  assert.equal(c.state.autoStatus, '');
});

test('runtime detection failure stops automatic work and can be retried', async () => {
  let broken = true;
  const { c, frame, captures } = await setup({ async detect() {
    if (broken) throw new Error('private error');
    return [face];
  } });
  await c.setCaptureMode('auto');
  await frame(0);
  assert.match(c.state.autoStatus, /重新检测/);
  assert.equal(captures(), 0);
  assert.ok(!JSON.stringify(c.state.logs).includes('private error'));
  broken = false;
  await c.retake();
  for (let t = 200; t <= 1800; t += 200) await frame(t);
  assert.equal(captures(), 1);
  c.disconnect();
});
