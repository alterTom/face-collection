import { computed, reactive, shallowRef } from 'vue';
import { FaceCaptureClient } from '../../web-sdk/face-capture.js';
import { createFaceDetector } from './face-detector.js';
import { createStabilityTracker } from './face-stability.js';

const errorMessages = {
  INVALID_ADDRESS: '请填写本机 WebSocket 地址，例如 ws://127.0.0.1:17653/face。',
  CONNECTION_FAILED: '连接失败，请确认 FaceCaptureAgent 已启动，且服务地址和端口正确。',
  CONNECTION_TIMEOUT: '连接超时，请确认本机采集服务已启动。',
  DISCONNECTED: '采集服务已断开，请重新连接。',
  NOT_CONNECTED: '请先连接本机采集服务。',
  CAMERA_BUSY: '摄像头被占用，请关闭其他采集页面或视频软件后重试。',
  CAMERA_OPEN_FAILED: '摄像头打开失败，请检查设备连接、权限和占用情况。',
  CAMERA_DISCONNECTED: '摄像头已断开，请检查设备连接后重新打开。',
  CAPTURE_TIMEOUT: '抓拍超时，请重新连接并打开摄像头。',
  COMMAND_TIMEOUT: '操作超时，请重新连接服务后重试。',
  IMAGE_TOO_LARGE: '照片超出服务的大小限制，请调整服务配置后重试。',
  INVALID_STATE: '摄像头状态已变化，请重新连接后再试。',
};

function createSdkClient(url, onClosed) {
  return new FaceCaptureClient({
    url,
    retryDelays: [], // Explicit retries avoid reconnecting after the user has disconnected.
    commandTimeoutMs: 15_000,
    webSocketFactory(address, protocol) {
      const socket = new WebSocket(address, protocol);
      socket.addEventListener('close', () => queueMicrotask(onClosed), { once: true });
      return socket;
    },
  });
}

export function createCaptureController({ createClient = createSdkClient, createDetector = createFaceDetector,
  now = () => performance.now() } = {}) {
  const state = reactive({
    url: 'ws://127.0.0.1:17653/face', connected: false, cameraOpen: false,
    deviceId: '', devices: [], busy: '', error: '', logs: [], resolution: '',
    captureMode: 'manual', autoStatus: '', autoComplete: false,
  });
  const photo = shallowRef(null);
  const photoUrl = computed(() => photo.value ? `data:image/jpeg;base64,${photo.value.base64}` : '');
  let client = null;
  let generation = 0;
  let previewElement = null;
  let logId = 0;
  let stopDetection = () => {};

  function stopAuto() {
    stopDetection();
    stopDetection = () => {};
  }

  async function startAuto() {
    stopAuto();
    if (state.captureMode !== 'auto' || !state.cameraOpen) return;
    state.autoComplete = false;
    state.autoStatus = '正在加载人脸检测…';
    const image = previewElement;
    const tracker = createStabilityTracker();
    let stopped = false, detecting = false, detector;
    const stop = () => {
      stopped = true;
      image.removeEventListener('load', onFrame);
      detector?.close();
    };
    stopDetection = stop;
    function failed() {
      if (stopped) return;
      stop();
      state.autoStatus = '人脸检测不可用，请切换手动拍照或点击重新检测';
      log(state.autoStatus, 'error');
    }
    async function onFrame() {
      if (stopped || detecting) return;
      if (state.busy || globalThis.document?.hidden) { tracker.reset(); return; }
      detecting = true;
      const timestamp = now();
      try {
        const faces = await detector.detect(image);
        if (stopped) return;
        if (state.busy || globalThis.document?.hidden || now() - timestamp > 750) { tracker.reset(); return; }
        const result = tracker.update(faces, timestamp);
        state.autoStatus = result.status;
        if (result.ready) {
          stop();
          await capture();
        }
      } catch { failed(); }
      finally { detecting = false; }
    }
    try {
      detector = createDetector();
      await detector.ready;
      if (stopped) return;
      state.autoStatus = '请面向摄像头';
      image.addEventListener('load', onFrame);
    } catch { failed(); }
  }

  function setCaptureMode(mode) {
    if (!['manual', 'auto'].includes(mode) || state.busy) return;
    stopAuto();
    state.captureMode = mode;
    state.autoStatus = '';
    state.autoComplete = false;
    return startAuto();
  }

  function retake() {
    if (state.busy || !state.cameraOpen) return;
    return startAuto();
  }

  function log(message, tone = 'info') {
    state.logs.unshift({ id: ++logId, time: new Date().toLocaleTimeString('zh-CN', { hour12: false }), message, tone });
    state.logs.splice(100);
  }

  function clearPreview() {
    stopAuto();
    state.autoStatus = '';
    state.autoComplete = false;
    if (previewElement) {
      previewElement.onload = null;
      previewElement.removeAttribute('src');
    }
    previewElement = null;
    state.cameraOpen = false;
    state.resolution = '';
  }

  function disconnect(announce = true) {
    generation += 1;
    const previous = client;
    client = null;
    state.connected = false;
    state.busy = '';
    state.devices = [];
    state.deviceId = '';
    clearPreview();
    previous?.disconnect();
    if (announce) {
      state.error = '';
      log('已断开连接，释放摄像头');
    }
  }

  async function run(label, action, resetOnError = false) {
    if (state.busy) return;
    const current = generation;
    state.busy = label;
    state.error = '';
    const active = () => current === generation;
    try {
      await action(active);
    } catch (error) {
      if (!active()) return;
      if (resetOnError) disconnect(false);
      const code = Object.hasOwn(errorMessages, error?.code) ? error.code : 'UNKNOWN_ERROR';
      state.error = errorMessages[code] ?? '操作失败，请重新连接服务后重试。';
      log(`${label}失败：${state.error} (${code})`, 'error');
    } finally {
      if (active()) state.busy = '';
    }
  }

  async function loadDevices(sdk, active) {
    const devices = await sdk.listDevices();
    if (!active()) return;
    state.devices = devices;
    if (!devices.some(device => device.id === state.deviceId)) state.deviceId = devices[0]?.id ?? '';
    if (devices.length === 0) state.error = '未发现摄像头，请接入设备后点击“刷新设备”。';
    log(`设备列表已更新，共 ${devices.length} 个摄像头`);
  }

  function connect() {
    if (state.connected) return;
    return run('连接服务', async active => {
      let address;
      try { address = new URL(state.url.trim()); } catch { throw { code: 'INVALID_ADDRESS' }; }
      if (!['ws:', 'wss:'].includes(address.protocol) || !['127.0.0.1', 'localhost'].includes(address.hostname)
        || address.username || address.password || address.hash) throw { code: 'INVALID_ADDRESS' };
      const current = generation;
      const sdk = createClient(address.href, () => {
        if (generation !== current || client !== sdk) return;
        const wasConnected = state.connected;
        // A failed initial connection is reported by connect() with a more useful error.
        if (!wasConnected) return;
        disconnect(false);
        state.error = errorMessages.DISCONNECTED;
        log(state.error, 'error');
      });
      client = sdk;
      await sdk.connect();
      if (!active()) { sdk.disconnect(); return; }
      state.connected = true;
      log('已连接本机采集服务', 'success');
      await loadDevices(sdk, active);
    }, true);
  }

  function refreshDevices() {
    if (!state.connected || state.cameraOpen) return;
    return run('刷新设备', active => loadDevices(client, active));
  }

  function open(image) {
    if (!state.connected || state.cameraOpen || !state.deviceId) return;
    return run('打开摄像头', async active => {
      const sdk = client;
      const result = await sdk.open({ deviceId: state.deviceId, width: 1280, height: 720, fps: 15 });
      if (!active()) return;
      previewElement = image;
      try {
        await sdk.startPreview(image, { fps: 5 });
      } catch (error) {
        try { await sdk.close(); } catch { /* Disconnect below releases any remaining lease. */ }
        throw error;
      }
      if (!active()) return;
      state.cameraOpen = true;
      state.resolution = `${result.width} × ${result.height}`;
      log('摄像头已打开，实时预览已开始', 'success');
      void startAuto();
    }, true);
  }

  function capture() {
    if (!state.cameraOpen || !state.connected) return;
    return run('抓拍照片', async active => {
      const result = await client.capture();
      if (!active()) return;
      photo.value = result;
      if (state.captureMode === 'auto') {
        stopAuto();
        state.autoComplete = true;
        state.autoStatus = '拍照完成';
      }
      log(`抓拍成功：${result.width} × ${result.height}，${(result.size / 1024).toFixed(1)} KB`, 'success');
    }, true);
  }

  function close() {
    if (!state.cameraOpen || !state.connected || state.busy) return;
    stopAuto();
    return run('关闭摄像头', async active => {
      await client.close();
      if (!active()) return;
      clearPreview();
      log('摄像头已关闭');
    }, true);
  }

  return { state, photo, photoUrl, connect, refreshDevices, open, capture, close, disconnect, setCaptureMode, retake,
    clearLogs: () => { state.logs = []; } };
}
