import { createLocalClient } from './client.js';

const messages = {
  NO_CAMERA: '未发现摄像头，请接入设备后重新拍照',
  CAMERA_BUSY: '摄像头被占用，请关闭其他采集程序后重新拍照',
  CAMERA_OPEN_FAILED: '无法打开摄像头，请检查设备后重新拍照',
  DISCONNECTED: '本机采集服务已断开，请重新拍照',
  CAMERA_DISCONNECTED: '摄像头已断开，请检查设备后重新拍照',
  DETECTION_FAILED: '人脸检测失败，请重新拍照',
  INVALID_PHOTO: '未获得有效照片，请重新拍照',
};
const statusMessages = {
  'no-face': '请将面部置于取景区域内',
  'multiple-faces': '请保持画面中只有一张人脸',
  stabilizing: '请保持稳定，正在自动拍照…',
  complete: '正在接收照片…',
};

// A session owns its connection deadline, socket, subscriptions and terminal result.
// Vue and test code only observe snapshots; they never manipulate its internal state.
export function createCaptureSession(options = {}, dependencies = {}) {
  const { createClient = createLocalClient, onState = () => {}, onResult = () => {} } = dependencies;
  const clock = dependencies.clock ?? {
    now: () => performance.now(),
    setTimeout: (fn, ms) => setTimeout(fn, ms), clearTimeout: id => clearTimeout(id),
  };
  const config = { serviceUrl: 'ws://127.0.0.1:17653/face', connectionTimeoutMs: 60_000,
    stableDurationMs: 1500, ...options };
  let state = { phase: 'idle', message: '', remainingSeconds: null };
  let client, image, deadline, timer, retryTimer;
  let version = 0, finished = false, started = false, connected = false;
  let unsubscribers = [];

  function update(values) { state = { ...state, ...values }; onState({ ...state }); }
  function clearTimers() {
    clock.clearTimeout(timer); clock.clearTimeout(retryTimer);
    timer = retryTimer = undefined;
  }
  function release() {
    version++;
    clearTimers();
    for (const unsubscribe of unsubscribers) unsubscribe();
    unsubscribers = [];
    const old = client; client = undefined; connected = false;
    old?.disconnect();
    if (image?.removeAttribute) { image.onload = null; image.removeAttribute('src'); }
  }
  function finish(result) {
    if (finished) return;
    finished = true;
    release();
    update({ phase: 'closed', remainingSeconds: null });
    onResult(result);
  }
  function fail(code) {
    if (finished) return;
    release();
    update({ phase: 'error', remainingSeconds: null,
      message: messages[code] ?? '采集操作失败，请重新拍照或退出认证' });
  }
  function expired() {
    if (clock.now() < deadline) return false;
    finish({ status: 'connection_failed', code: 'AGENT_CONNECTION_TIMEOUT',
      message: `${Math.ceil(config.connectionTimeoutMs / 1000)}秒内未能连接本机采集服务` });
    return true;
  }
  function tick() {
    if (finished || connected || expired()) return;
    const left = deadline - clock.now();
    update({ remainingSeconds: Math.ceil(left / 1000) });
    timer = clock.setTimeout(tick, Math.min(250, left));
  }
  function acceptPhoto(data) {
    try {
      if (data?.mimeType !== 'image/jpeg' || !data.base64 || !(data.width > 0) || !(data.height > 0)) throw new Error();
      const bytes = Uint8Array.from(atob(data.base64), c => c.charCodeAt(0));
      if (bytes.length < 4 || bytes[0] !== 255 || bytes[1] !== 216) throw new Error();
      const blob = new Blob([bytes], { type: 'image/jpeg' });
      finish({ status: 'success', photo: { base64: data.base64, blob, mimeType: 'image/jpeg',
        width: data.width, height: data.height, size: blob.size, capturedAt: data.capturedAt } });
    } catch { fail('INVALID_PHOTO'); }
  }
  async function attempt() {
    if (finished || expired()) return;
    const current = ++version;
    const active = () => !finished && current === version;
    let sdk;
    try {
      sdk = createClient(config.serviceUrl, () => { if (active() && connected) fail('DISCONNECTED'); });
      client = sdk;
      await sdk.connect();
      if (!active()) { sdk.disconnect(); return; }
      // Check monotonic time as well as the timer: background tabs may throttle timers.
      if (expired()) return;
      connected = true;
      clearTimers();
      update({ phase: 'opening', remainingSeconds: null, message: '已连接，正在打开摄像头…' });
      const devices = await sdk.listDevices();
      if (!active()) return;
      const device = config.deviceId == null ? devices[0] : devices.find(d => d.id === config.deviceId);
      if (!device) { fail('NO_CAMERA'); return; }
      const listen = (type, fn) => sdk.on(type, data => { if (active()) fn(data); });
      unsubscribers = [
        listen('auto.status', data => update({ message: statusMessages[data.status] ?? '请面向摄像头' })),
        listen('auto.capture', acceptPhoto),
        listen('auto.error', data => fail(data.cameraClosed ? 'CAMERA_DISCONNECTED' : 'DETECTION_FAILED')),
      ];
      await sdk.open({ deviceId: device.id, width: 1280, height: 720, fps: 15,
        captureMode: 'auto', stableDurationMs: config.stableDurationMs });
      if (!active()) return;
      await sdk.startPreview(image, { fps: 5 });
      if (!active()) return;
      update({ phase: 'capturing', message: '请面向摄像头，保持稳定' });
    } catch (error) {
      if (!active()) return;
      if (connected) { fail(error?.code); return; }
      // Invalidate callbacks from this failed socket before disconnecting it.
      version++; sdk?.disconnect(); client = undefined;
      if (expired()) return;
      update({ message: '暂未连接到本机服务，正在重试…' });
      retryTimer = clock.setTimeout(attempt, Math.min(1000, deadline - clock.now()));
    }
  }
  function connect() {
    deadline = clock.now() + config.connectionTimeoutMs;
    update({ phase: 'connecting', message: '正在连接本机采集服务…',
      remainingSeconds: Math.ceil(config.connectionTimeoutMs / 1000) });
    tick(); void attempt();
  }
  function validate() {
    const url = new URL(config.serviceUrl);
    if (!['ws:', 'wss:'].includes(url.protocol) || !['127.0.0.1', 'localhost'].includes(url.hostname)
      || url.username || url.password || url.hash
      || !Number.isInteger(config.connectionTimeoutMs) || config.connectionTimeoutMs < 1 || config.connectionTimeoutMs > 2_147_483_647
      || !Number.isInteger(config.stableDurationMs) || config.stableDurationMs < 500 || config.stableDurationMs > 10_000
      || (config.deviceId != null && typeof config.deviceId !== 'string')) throw new Error();
  }
  function start(element) {
    if (started || finished) return;
    started = true; image = element;
    try { validate(); } catch {
      finish({ status: 'error', code: 'INVALID_OPTIONS', message: '采集组件配置无效，请检查服务地址和时间参数' });
      return;
    }
    connect();
  }
  async function retake() {
    if (finished) return;
    if (state.phase === 'error') { release(); connect(); return; }
    if (state.phase !== 'capturing') return;
    const current = version;
    update({ phase: 'rearming', message: '正在重新开始拍照…' });
    try {
      await client.rearm();
      if (!finished && current === version) update({ phase: 'capturing', message: '请面向摄像头，保持稳定' });
    } catch (error) { if (!finished && current === version) fail(error?.code); }
  }
  return { start, retake, cancel: () => finish({ status: 'cancelled' }) };
}
