import { computed, reactive, shallowRef } from 'vue';
import { FaceCaptureClient } from '../../web-sdk/face-capture.js';

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

export function createCaptureController({ createClient = createSdkClient } = {}) {
  const location = globalThis.location;
  const defaultUrl = location?.hostname === '127.0.0.1' && location.pathname.startsWith('/test/')
    ? `${location.protocol === 'https:' ? 'wss:' : 'ws:'}//${location.host}/face`
    : 'ws://127.0.0.1:17653/face';
  const state = reactive({
    url: defaultUrl, connected: false, cameraOpen: false,
    deviceId: '', devices: [], busy: '', error: '', logs: [], resolution: '',
    captureMode: 'manual', stableDurationMs: 1500, autoStatus: '', autoComplete: false,
  });
  const photo = shallowRef(null);
  const photoUrl = computed(() => photo.value ? `data:image/jpeg;base64,${photo.value.base64}` : '');
  let client = null;
  let generation = 0;
  let cameraGeneration = 0;
  let previewElement = null;
  let logId = 0;
  let subscriptions = [];
  let acceptAuto = false;

  function resetAuto() {
    state.autoComplete = false;
    state.autoStatus = state.captureMode === 'auto' ? '请面向摄像头' : '';
  }

  function subscribe(sdk, current) {
    const listen = (type, callback) => sdk.on?.(type, data => {
      if (generation !== current || client !== sdk) return;
      if ((type === 'auto.error' && data.cameraClosed) || (acceptAuto && state.captureMode === 'auto')) callback(data);
    });
    subscriptions = [
      listen('auto.status', data => {
        state.autoStatus = {
          'no-face': '请面向摄像头', 'multiple-faces': '请保持画面中只有一张人脸',
          stabilizing: '请保持稳定…', complete: '拍照完成',
        }[data.status] ?? state.autoStatus;
      }),
      listen('auto.capture', data => {
        photo.value = data;
        state.autoComplete = true;
        state.autoStatus = '拍照完成';
        log(`自动抓拍成功：${data.width} × ${data.height}`, 'success');
      }),
      listen('auto.error', data => {
        acceptAuto = false;
        if (data.cameraClosed) {
          clearPreview();
          state.error = errorMessages.CAMERA_DISCONNECTED;
        } else {
          state.autoStatus = '人脸检测不可用，请切换手动拍照或点击重新检测';
        }
        log(state.error || state.autoStatus, 'error');
      }),
    ].filter(Boolean);
  }

  function setCaptureMode(mode) {
    if (!['manual', 'auto'].includes(mode) || state.busy) return;
    state.captureMode = mode;
    resetAuto();
    if (!state.cameraOpen) return;
    acceptAuto = mode === 'auto';
    return run('切换拍照模式', async () => {
      await client.setCaptureMode({ captureMode: mode, stableDurationMs: state.stableDurationMs });
    }, true);
  }

  function retake() {
    if (state.busy || !state.cameraOpen || state.captureMode !== 'auto') return;
    resetAuto();
    acceptAuto = true;
    return run('重新检测', async () => { await client.rearm(); }, true);
  }

  function log(message, tone = 'info') {
    state.logs.unshift({ id: ++logId, time: new Date().toLocaleTimeString('zh-CN', { hour12: false }), message, tone });
    state.logs.splice(100);
  }

  function clearPreview() {
    cameraGeneration += 1;
    acceptAuto = false;
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
    for (const unsubscribe of subscriptions) unsubscribe();
    subscriptions = [];
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
      subscribe(sdk, current);
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
      const cameraRound = cameraGeneration;
      resetAuto();
      acceptAuto = state.captureMode === 'auto';
      const result = await sdk.open({ deviceId: state.deviceId, width: 1280, height: 720, fps: 15,
        captureMode: state.captureMode, stableDurationMs: state.stableDurationMs });
      if (!active() || cameraRound !== cameraGeneration) return;
      previewElement = image;
      try {
        await sdk.startPreview(image, { fps: 5 });
      } catch (error) {
        try { await sdk.close(); } catch { /* Disconnect below releases any remaining lease. */ }
        throw error;
      }
      if (!active() || cameraRound !== cameraGeneration) return;
      state.cameraOpen = true;
      state.resolution = `${result.width} × ${result.height}`;
      log('摄像头已打开，实时预览已开始', 'success');
    }, true);
  }

  function capture() {
    if (!state.cameraOpen || !state.connected) return;
    return run('抓拍照片', async active => {
      const result = await client.capture();
      if (!active()) return;
      photo.value = result;
      if (state.captureMode === 'auto') {
        acceptAuto = false;
        state.autoComplete = true;
        state.autoStatus = '拍照完成';
      }
      log(`抓拍成功：${result.width} × ${result.height}，${(result.size / 1024).toFixed(1)} KB`, 'success');
    }, true);
  }

  function close() {
    if (!state.cameraOpen || !state.connected || state.busy) return;
    acceptAuto = false;
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
