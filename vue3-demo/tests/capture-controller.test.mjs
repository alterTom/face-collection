import test from 'node:test';
import assert from 'node:assert/strict';
import { createCaptureController } from '../src/capture-controller.js';

function setup(overrides = {}) {
  let onClosed;
  const camera = {
    connected: false,
    async connect() { this.connected = true; },
    async listDevices() { return [{ id: '0', name: 'Camera 0' }]; },
    async open() { return { width: 1280, height: 720 }; },
    async startPreview() {},
    async close() {},
    async capture() { return { width: 2, height: 2, size: 4, base64: '/9j/2Q==', mimeType: 'image/jpeg' }; },
    disconnect() { this.connected = false; },
    ...overrides,
  };
  const controller = createCaptureController({ createClient: (_, closed) => { onClosed = closed; return camera; } });
  return { controller, closeConnection: () => onClosed(), camera };
}

test('connect, preview and capture expose a photo without putting its contents in logs', async () => {
  const { controller: c } = setup();
  await c.connect();
  assert.equal(c.state.connected, true);
  assert.equal(c.state.deviceId, '0');
  await c.open({ removeAttribute() {} });
  assert.equal(c.state.cameraOpen, true);
  await c.capture();
  assert.equal(c.photo.value.width, 2);
  assert.ok(c.photoUrl.value.startsWith('data:image/jpeg;base64,'));
  assert.ok(!JSON.stringify(c.state.logs).includes('/9j/2Q=='));
  await c.close();
  assert.equal(c.state.cameraOpen, false);
  c.disconnect();
});

test('preview failure rolls back the open camera and allows opening again', async () => {
  const { controller: c } = setup({ async startPreview() { throw { code: 'CAMERA_DISCONNECTED', message: 'private-content' }; } });
  await c.connect();
  await c.open({ removeAttribute() {} });
  assert.equal(c.state.cameraOpen, false);
  assert.equal(c.state.busy, '');
  assert.match(c.state.error, /摄像头/);
  assert.ok(!JSON.stringify(c.state.logs).includes('private-content'));
  c.disconnect();
});

test('unexpected disconnect resets camera and preview state', async () => {
  const { controller: c, closeConnection } = setup();
  await c.connect();
  await c.open({ removeAttribute() {} });
  closeConnection();
  assert.equal(c.state.connected, false);
  assert.equal(c.state.cameraOpen, false);
  assert.deepEqual([...c.state.devices], []);
  assert.match(c.state.error, /断开/);
});

test('a capture response arriving after disconnect cannot restore stale state', async () => {
  let finish;
  const { controller: c } = setup({ capture: () => new Promise(resolve => { finish = resolve; }) });
  await c.connect();
  await c.open({ removeAttribute() {} });
  const capture = c.capture();
  c.disconnect();
  finish({ width: 2, height: 2, base64: '/9j/2Q==' });
  await capture;
  assert.equal(c.photo.value, null);
  assert.equal(c.state.connected, false);
  assert.equal(c.state.busy, '');
});

test('non-loopback addresses are rejected before connecting', async () => {
  const { controller: c, camera } = setup();
  c.state.url = 'ws://example.com/face';
  await c.connect();
  assert.equal(camera.connected, false);
  assert.match(c.state.error, /本机/);
});

test('no camera keeps the connection usable for another device refresh', async () => {
  const { controller: c } = setup({ async listDevices() { return []; } });
  await c.connect();
  assert.equal(c.state.connected, true);
  assert.equal(c.state.deviceId, '');
  assert.match(c.state.error, /未发现/);
  c.disconnect();
});
