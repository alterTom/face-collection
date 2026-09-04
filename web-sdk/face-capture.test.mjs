import test from 'node:test';
import assert from 'node:assert/strict';
import { FaceCaptureClient, FaceCaptureError } from './face-capture.js';

class FakeWebSocket {
  constructor() {
    this.readyState = 0;
    this.sent = [];
    this.binaryType = '';
    this.closeCalls = [];
  }

  send(value) {
    if (this.readyState !== 1) throw new Error('socket is not open');
    this.sent.push(value);
  }

  close(code, reason) {
    this.closeCalls.push({ code, reason });
    this.readyState = 3;
    this.onclose?.({ code: code ?? 1000, reason: reason ?? '' });
  }

  emitOpen() {
    this.readyState = 1;
    this.onopen?.({});
  }

  emitJson(value) {
    this.onmessage?.({ data: JSON.stringify(value) });
  }

  emitBinary(value) {
    this.onmessage?.({ data: value });
  }

  get lastRequest() {
    return JSON.parse(this.sent.at(-1));
  }
}

async function connectedClient(options = {}) {
  const socket = new FakeWebSocket();
  const client = new FaceCaptureClient({
    webSocketFactory: () => socket,
    retryDelays: [],
    heartbeatIntervalMs: 60_000,
    ...options,
  });
  const pending = client.connect();
  socket.emitOpen();
  await pending;
  return { client, socket };
}

test('connect requests the required protocol and configures binary blobs', async () => {
  const socket = new FakeWebSocket();
  let argumentsSeen;
  const client = new FaceCaptureClient({
    webSocketFactory: (...args) => {
      argumentsSeen = args;
      return socket;
    },
    retryDelays: [],
  });

  const pending = client.connect();
  socket.emitOpen();
  await pending;

  assert.deepEqual(argumentsSeen, ['ws://127.0.0.1:17653/face', 'face-capture.v1']);
  assert.equal(socket.binaryType, 'blob');
  client.disconnect();
});

test('responses are correlated by requestId even when returned out of order', async () => {
  const { client, socket } = await connectedClient();

  const infoPending = client.getSystemInfo();
  const infoRequest = JSON.parse(socket.sent.at(-1));
  const devicesPending = client.listDevices();
  const devicesRequest = JSON.parse(socket.sent.at(-1));
  socket.emitJson({ type: 'device.list.result', requestId: devicesRequest.requestId, data: { devices: [{ id: '0' }] } });
  socket.emitJson({ type: 'system.info.result', requestId: infoRequest.requestId, data: { agentVersion: '1.0.0' } });

  assert.deepEqual(await devicesPending, [{ id: '0' }]);
  assert.equal((await infoPending).agentVersion, '1.0.0');
  client.disconnect();
});

test('capture resolves the matching request with raw Base64', async () => {
  const { client, socket } = await connectedClient();

  const pending = client.capture();
  socket.emitJson({
    type: 'capture.result',
    requestId: socket.lastRequest.requestId,
    data: {
      mimeType: 'image/jpeg', width: 2, height: 2, size: 4,
      capturedAt: '2026-09-03T10:20:30+08:00', base64: '/9j/',
    },
  });

  assert.equal((await pending).base64, '/9j/');
  client.disconnect();
});

test('server errors reject with stable FaceCaptureError fields', async () => {
  const { client, socket } = await connectedClient();

  const pending = client.open({ deviceId: '0' });
  socket.emitJson({
    type: 'error',
    requestId: socket.lastRequest.requestId,
    error: { code: 'CAMERA_BUSY', message: 'busy', retryable: true },
  });

  await assert.rejects(pending, error =>
    error instanceof FaceCaptureError
      && error.code === 'CAMERA_BUSY'
      && error.retryable === true);
  client.disconnect();
});

test('command timeout rejects a pending request', async () => {
  const { client } = await connectedClient({ commandTimeoutMs: 15 });

  await assert.rejects(client.capture(), error =>
    error instanceof FaceCaptureError && error.code === 'COMMAND_TIMEOUT');
  client.disconnect();
});

test('binary preview revokes every superseded object URL and the final URL on disconnect', async () => {
  const created = [];
  const revoked = [];
  const urlApi = {
    createObjectURL(blob) {
      created.push(blob);
      return `blob:test-${created.length}`;
    },
    revokeObjectURL(url) {
      revoked.push(url);
    },
  };
  const image = { src: '', onload: null };
  const { client, socket } = await connectedClient({ urlApi });

  const started = client.startPreview(image);
  socket.emitJson({
    type: 'preview.start.result',
    requestId: socket.lastRequest.requestId,
    data: { fps: 5 },
  });
  await started;
  const jpeg = new Blob([new Uint8Array([0xff, 0xd8, 0xff, 0xd9])], { type: 'image/jpeg' });
  socket.emitBinary(jpeg);
  socket.emitBinary(jpeg);
  socket.emitBinary(jpeg);

  assert.equal(created[0], jpeg);
  assert.equal(image.src, 'blob:test-3');
  image.onload();
  assert.deepEqual(revoked, ['blob:test-1', 'blob:test-2']);
  client.disconnect();
  assert.deepEqual(revoked, ['blob:test-1', 'blob:test-2', 'blob:test-3']);
});

test('disconnect rejects all pending commands and closes the socket', async () => {
  const { client, socket } = await connectedClient();
  const capture = client.capture();
  const devices = client.listDevices();

  client.disconnect();

  await assert.rejects(capture, error => error.code === 'DISCONNECTED');
  await assert.rejects(devices, error => error.code === 'DISCONNECTED');
  assert.equal(socket.closeCalls.length, 1);
});
