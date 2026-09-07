// A classic worker supports MediaPipe's WASM importScripts loader.
export function createFaceDetector() {
  const assetRoot = new URL(`${import.meta.env.BASE_URL}face-detection/`, document.baseURI).href;
  const worker = new Worker(`${assetRoot}worker/face-detector.js`);
  let pending = null;
  let closed = false;
  function request(data, transfer = []) {
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => fail(), 20000);
      pending = { resolve, reject, timer };
      try { worker.postMessage(data, transfer); } catch { fail(); }
    });
  }
  function fail() {
    if (!pending) return;
    clearTimeout(pending.timer);
    pending.reject(new Error('Face detection unavailable'));
    pending = null;
  }
  worker.onerror = fail;
  worker.onmessage = ({ data }) => {
    if (data.type === 'error') { fail(); return; }
    if (!pending) return;
    clearTimeout(pending.timer);
    pending.resolve(data.faces);
    pending = null;
  };
  const ready = request({ type: 'init', assetRoot });
  return {
    ready,
    async detect(image) {
      await ready;
      if (closed) throw new Error('Detector closed');
      const bitmap = await createImageBitmap(image);
      if (closed) { bitmap.close(); throw new Error('Detector closed'); }
      return request({ type: 'detect', bitmap }, [bitmap]);
    },
    close() { closed = true; fail(); worker.terminate(); },
  };
}
