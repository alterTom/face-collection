import { FaceCaptureClient } from '../../web-sdk/face-capture.js';

export function createLocalClient(url, onClosed) {
  return new FaceCaptureClient({
    url, retryDelays: [], connectTimeoutMs: 2000, commandTimeoutMs: 10_000,
    webSocketFactory(address, protocol) {
      const socket = new WebSocket(address, protocol);
      socket.addEventListener('close', () => queueMicrotask(onClosed), { once: true });
      return socket;
    },
  });
}
