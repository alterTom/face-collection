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
