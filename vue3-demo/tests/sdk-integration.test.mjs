import test from 'node:test';
import assert from 'node:assert/strict';
import { setTimeout as delay } from 'node:timers/promises';
import { createCaptureController } from '../src/capture-controller.js';
import { startTestAgent } from './fixtures/agent.mjs';

test('real SDK and WebSocket complete preview, capture and unexpected disconnect', async () => {
  const agent = await startTestAgent();
  const c = createCaptureController();
  try {
    c.state.url = agent.url;
    await c.connect();
    assert.equal(c.state.connected, true);
    const image = { src: '', removeAttribute() { this.src = ''; } };
    await c.open(image);
    assert.equal(c.state.cameraOpen, true);
    await c.capture();
    assert.equal(c.photo.value.width, 640);
    assert.equal(c.photo.value.height, 480);
    assert.ok(c.photo.value.size > 100);
    assert.ok(!JSON.stringify(c.state.logs).includes(c.photo.value.base64));
    agent.disconnectClients();
    for (let attempt = 0; attempt < 50 && c.state.connected; attempt++) await delay(10);
    assert.equal(c.state.connected, false);
    assert.equal(c.state.cameraOpen, false);
    assert.equal(image.src, '');
  } finally { c.disconnect(false); await agent.close(); }
});

test('real SDK camera-busy response produces a safe retryable UI state', async () => {
  const agent = await startTestAgent();
  const c = createCaptureController();
  try {
    c.state.url = agent.url.replace('/face', '/busy');
    await c.connect();
    await c.open({ removeAttribute() {} });
    assert.match(c.state.error, /占用/);
    assert.equal(c.state.cameraOpen, false);
    assert.equal(c.state.connected, false);
    assert.equal(c.state.busy, '');
  } finally { c.disconnect(false); await agent.close(); }
});

test('Agent auto events work without preview and rearm creates another round', async () => {
  const { FaceCaptureClient } = await import('../../web-sdk/face-capture.js');
  const agent = await startTestAgent();
  const client = new FaceCaptureClient({ url: agent.url, retryDelays: [] });
  const photos = [];
  client.on('auto.capture', data => photos.push(data));
  try {
    await client.connect();
    await client.open({ deviceId: '0', captureMode: 'auto', stableDurationMs: 500 });
    for (let i = 0; i < 50 && photos.length < 1; i++) await delay(10);
    assert.equal(photos.length, 1);
    await client.rearm();
    for (let i = 0; i < 50 && photos.length < 2; i++) await delay(10);
    assert.equal(photos.length, 2);
    assert.notEqual(photos[0].roundId, photos[1].roundId);
  } finally { client.disconnect(); await agent.close(); }
});
