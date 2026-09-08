import test from 'node:test';
import assert from 'node:assert/strict';
import { createCaptureController } from '../src/capture-controller.js';
async function setup() {
  const handlers = new Map(), calls = [];
  const client = {
    on(type, callback) { handlers.set(type, callback); return () => handlers.delete(type); },
    async connect() {}, async listDevices() { return [{ id: '0' }]; },
    async open(options) { calls.push(options); return { width: 640, height: 480 }; },
    async startPreview() {}, async close() {}, disconnect() {},
    async setCaptureMode(options) { calls.push(options); }, async rearm() { calls.push('rearm'); },
    async capture() { calls.push('capture'); return { width: 1, height: 1, size: 1, base64: 'AA==' }; },
  };
  const c = createCaptureController({ createClient: () => client });
  await c.connect();
  await c.open({ removeAttribute() {} });
  return { c, calls, emit: (type, data) => handlers.get(type)?.(data), handlers };
}
test('camera closure is handled in manual mode and after detector failure', async () => {
  for (const detectorFailed of [false, true]) {
    const { c, emit } = await setup();
    if (detectorFailed) {
      await c.setCaptureMode('auto');
      emit('auto.error', { cameraClosed: false, code: 'FACE_DETECTION_FAILED' });
    }
    emit('auto.error', { cameraClosed: true, code: 'CAMERA_DISCONNECTED' });
    assert.equal(c.state.cameraOpen, false);
    assert.match(c.state.error, /摄像头/);
    c.disconnect();
  }
});
test('auto selection delegates to Agent and pushed capture updates photo without manual capture', async () => {
  const { c, calls, emit } = await setup();
  assert.equal(calls[0].captureMode, 'manual');
  await c.setCaptureMode('auto');
  assert.deepEqual(calls[1], { captureMode: 'auto', stableDurationMs: 1500 });
  emit('auto.status', { status: 'stabilizing' });
  assert.match(c.state.autoStatus, /稳定/);
  emit('auto.capture', { width: 640, height: 480, size: 1, base64: 'AA==' });
  assert.equal(c.photo.value.base64, 'AA==');
  assert.equal(c.state.autoComplete, true);
  assert.ok(!calls.includes('capture'));
  await c.retake();
  assert.equal(calls.at(-1), 'rearm');
  assert.equal(c.state.autoComplete, false);
  c.disconnect();
});
test('auto error is sanitized and detector failure preserves manual capture', async () => {
  const { c, calls, emit } = await setup();
  await c.setCaptureMode('auto');
  emit('auto.error', { code: 'DETECTION_FAILED', message: 'private content', retryable: true, cameraClosed: false });
  assert.match(c.state.autoStatus, /手动/);
  assert.ok(!JSON.stringify(c.state).includes('private content'));
  await c.setCaptureMode('manual');
  await c.capture();
  assert.equal(calls.at(-1), 'capture');
  c.disconnect();
});
test('camera error clears preview and disconnect unsubscribes all events', async () => {
  const { c, emit, handlers } = await setup();
  await c.setCaptureMode('auto');
  emit('auto.error', { code: 'CAMERA_DISCONNECTED', cameraClosed: true });
  assert.equal(c.state.cameraOpen, false);
  assert.match(c.state.error, /摄像头/);
  c.disconnect();
  assert.equal(handlers.size, 0);
});

test('camera-closed event during preview startup cannot be overwritten by its late success', async () => {
  const handlers = new Map();
  let finishPreview;
  const client = {
    on(type, cb) { handlers.set(type, cb); return () => handlers.delete(type); },
    async connect() {}, async listDevices() { return [{ id: '0' }]; },
    async open() { return { width: 640, height: 480 }; },
    startPreview() { return new Promise(resolve => { finishPreview = resolve; }); },
    disconnect() {},
  };
  const c = createCaptureController({ createClient: () => client });
  await c.connect();
  await c.setCaptureMode('auto');
  const opening = c.open({ removeAttribute() {} });
  await new Promise(resolve => setImmediate(resolve));
  handlers.get('auto.error')({ code: 'CAMERA_DISCONNECTED', cameraClosed: true });
  finishPreview();
  await opening;
  assert.equal(c.state.cameraOpen, false);
  c.disconnect();
});
