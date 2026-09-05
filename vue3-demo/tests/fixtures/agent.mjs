import { WebSocketServer } from 'ws';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';

// Test-only protocol peer. The frontend always uses the real SDK and WebSocket API.
export async function startTestAgent(port = 0) {
  const jpeg = await readFile(new URL('./frame.jpg', import.meta.url));
  const server = new WebSocketServer({ host: '127.0.0.1', port,
    handleProtocols: protocols => protocols.has('face-capture.v1') ? 'face-capture.v1' : false });
  await new Promise((done, reject) => { server.once('listening', done); server.once('error', reject); });
  server.on('connection', (socket, request) => {
    let opened = false;
    let timer;
    const stop = () => { clearInterval(timer); timer = null; };
    socket.on('close', stop);
    socket.on('message', raw => {
      const message = JSON.parse(raw.toString());
      const send = (type, data) => socket.send(JSON.stringify({ type, requestId: message.requestId, data }));
      const error = code => socket.send(JSON.stringify({ type: 'error', requestId: message.requestId, error: { code, message: 'test error', retryable: true } }));
      switch (message.type) {
        case 'ping': send('pong', {}); break;
        case 'device.list': send('device.list.result', { devices: [{ id: '0', name: '测试摄像头（合成画面）' }] }); break;
        case 'camera.open':
          if (request.url === '/busy') { error('CAMERA_BUSY'); break; }
          opened = true;
          send('camera.open.result', { width: 640, height: 480, fps: 15 });
          break;
        case 'preview.start':
          if (!opened) { error('INVALID_STATE'); break; }
          send('preview.start.result', { fps: 5 });
          socket.send(jpeg);
          stop();
          timer = setInterval(() => { if (socket.readyState === 1) socket.send(jpeg); }, 200);
          timer.unref();
          break;
        case 'capture':
          if (!opened) { error('INVALID_STATE'); break; }
          send('capture.result', { width: 640, height: 480, size: jpeg.length, mimeType: 'image/jpeg', base64: jpeg.toString('base64'), capturedAt: new Date().toISOString() });
          break;
        case 'camera.close': opened = false; stop(); send('camera.close.result', {}); break;
        default: error('INVALID_MESSAGE');
      }
    });
  });
  return {
    url: `ws://127.0.0.1:${server.address().port}/face`,
    disconnectClients() { for (const socket of server.clients) socket.close(1001, 'Test disconnect'); },
    async close() {
      for (const socket of server.clients) socket.terminate();
      await new Promise(done => server.close(done));
    },
  };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const agent = await startTestAgent(Number(process.env.TEST_AGENT_PORT ?? 17654));
  console.log(`Test agent (synthetic image only): ${agent.url}`);
}
