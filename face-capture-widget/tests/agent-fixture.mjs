import { WebSocketServer } from 'ws';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
import { randomUUID } from 'node:crypto';

// Synthetic protocol peer only; never used by the production package.
export async function startAgent({ port = 0, captureDelay = 60, busyOnce = false } = {}) {
  const jpeg = await readFile(new URL('../../vue3-demo/tests/fixtures/frame.jpg', import.meta.url));
  const server = new WebSocketServer({ host: '127.0.0.1', port,
    handleProtocols: protocols => protocols.has('face-capture.v1') ? 'face-capture.v1' : false });
  await new Promise((done, reject) => { server.once('listening', done); server.once('error', reject); });
  const commands = [];
  server.on('connection', (socket, request) => {
    let captureTimer, previewTimer, roundId, open = false;
    const sendEvent = (type, data) => { if (socket.readyState === 1) socket.send(JSON.stringify({ type, event: true, data: { ...data, roundId } })); };
    const begin = () => {
      clearTimeout(captureTimer);
      sendEvent('auto.status', { status: 'stabilizing' });
      if (request.url === '/idle') return;
      captureTimer = setTimeout(() => sendEvent('auto.capture', {
        base64: jpeg.toString('base64'), mimeType: 'image/jpeg', width: 640, height: 480,
        size: jpeg.length, capturedAt: new Date().toISOString(),
      }), captureDelay);
    };
    socket.on('message', raw => {
      const message = JSON.parse(raw.toString());
      commands.push({ type: message.type, captureMode: message.captureMode, deviceId: message.deviceId });
      const reply = data => socket.send(JSON.stringify({ type: `${message.type}.result`, requestId: message.requestId, data }));
      const error = code => socket.send(JSON.stringify({ type: 'error', requestId: message.requestId, error: { code, message: 'test-only internal details' } }));
      if (message.type === 'device.list') reply({ devices: [{ id: '0', name: '合成测试摄像头' }] });
      else if (message.type === 'camera.open' || message.type === 'capture.rearm') {
        if (request.url === '/busy' || busyOnce) { busyOnce = false; error('CAMERA_BUSY'); return; }
        open = true; roundId = randomUUID();
        reply({ roundId, captureMode: 'auto', stableDurationMs: 1500, width: 640, height: 480 }); begin();
      } else if (message.type === 'preview.start') {
        if (!open) { error('INVALID_STATE'); return; }
        reply({ fps: 5 }); socket.send(jpeg);
        clearInterval(previewTimer);
        previewTimer = setInterval(() => { if (socket.readyState === 1) socket.send(jpeg); }, 200);
      } else if (message.type === 'camera.close') {
        open = false; clearTimeout(captureTimer); clearInterval(previewTimer); reply({});
      } else if (message.type === 'ping') {
        socket.send(JSON.stringify({ type: 'pong', requestId: message.requestId, data: {} }));
      } else error('INVALID_MESSAGE');
    });
    socket.on('close', () => { clearTimeout(captureTimer); clearInterval(previewTimer); });
  });
  return {
    url: `ws://127.0.0.1:${server.address().port}/face`, commands,
    get connections() { return server.clients.size; },
    async close() { for (const client of server.clients) client.terminate(); await new Promise(done => server.close(done)); },
  };
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const agent = await startAgent({ port: 17658, captureDelay: 12_000 });
  console.log(`Synthetic Agent: ${agent.url}; /idle: preview only; /busy: device busy`);
}
