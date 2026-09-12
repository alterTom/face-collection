const DEFAULT_URL = 'ws://127.0.0.1:17653/face';
const REQUIRED_PROTOCOL = 'face-capture.v1';

/** 统一封装本地连接错误与 Agent 协议错误，业务方可按 code 和 retryable 处理。 */
export class FaceCaptureError extends Error {
  constructor(code, message, retryable = false) {
    super(message);
    this.name = 'FaceCaptureError';
    this.code = code;
    this.retryable = Boolean(retryable);
  }
}

/** 浏览器采集入口：管理命令响应、JPEG 预览、心跳和自动拍照事件。 */
export class FaceCaptureClient {
  constructor(options = {}) {
    this.url = options.url ?? DEFAULT_URL;
    this._webSocketFactory = options.webSocketFactory
      ?? ((url, protocol) => new WebSocket(url, protocol));
    this._urlApi = options.urlApi ?? globalThis.URL;
    this._connectTimeoutMs = options.connectTimeoutMs ?? 2_000;
    this._commandTimeoutMs = options.commandTimeoutMs ?? 10_000;
    this._retryDelays = options.retryDelays ?? [500, 1_500];
    this._heartbeatIntervalMs = options.heartbeatIntervalMs ?? 15_000;
    // 用 requestId 关联请求与响应；每个待完成请求保存自己的超时和 Promise 回调。
    this._pending = new Map();
    this._counter = 0;
    this._listeners = new Map();
    this._roundId = null;
    this._roundVersion = 0;
    this._socket = null;
    this._connectPromise = null;
    this._heartbeatTimer = null;
    this._heartbeatPending = false;
    this._previewElement = null;
    this._previewUrl = null;
    this._stalePreviewUrls = new Set();
  }

  get connected() {
    return this._socket?.readyState === 1;
  }

  /** 建立连接；重复调用共享正在进行的连接任务，失败时按配置的间隔重试。 */
  connect() {
    if (this.connected) return Promise.resolve();
    if (this._connectPromise) return this._connectPromise;

    this._connectPromise = this._connectWithRetries()
      .finally(() => {
        this._connectPromise = null;
      });
    return this._connectPromise;
  }

  async getSystemInfo() {
    return this._sendCommand('system.info');
  }

  async listDevices() {
    const data = await this._sendCommand('device.list');
    return data.devices ?? [];
  }

  /** 打开摄像头。captureMode 为 manual 或 auto；stableDurationMs 为 500–10000 的整数毫秒。 */
  async open(options = {}) {
    return this._sendRoundCommand('camera.open', {
      deviceId: options.deviceId,
      width: options.width,
      height: options.height,
      fps: options.fps,
      captureMode: options.captureMode,
      stableDurationMs: options.stableDurationMs,
    });
  }

  /** 订阅 auto.status / auto.capture / auto.error，返回取消订阅函数。 */
  on(eventType, callback) {
    if (!['auto.status', 'auto.capture', 'auto.error'].includes(eventType) || typeof callback !== 'function') {
      throw new TypeError('A supported auto event and callback are required.');
    }
    const listeners = this._listeners.get(eventType) ?? new Set();
    this._listeners.set(eventType, listeners);
    listeners.add(callback);
    return () => listeners.delete(callback);
  }

  /** 摄像头打开后切换拍照模式，服务端会创建新的自动拍照轮次。 */
  setCaptureMode(options = {}) {
    return this._sendRoundCommand('camera.setCaptureMode', {
      captureMode: options.captureMode, stableDurationMs: options.stableDurationMs,
    });
  }

  /** 自动模式下开始新一轮检测，用于拍完重拍或检测失败后重试。 */
  rearm() {
    return this._sendRoundCommand('capture.rearm');
  }

  _invalidateRound() {
    this._roundId = null;
    this._roundVersion += 1;
  }

  // 发命令前使旧轮次失效；只有最新轮次命令的响应才能重新绑定 roundId。
  _sendRoundCommand(type, fields = {}) {
    this._invalidateRound();
    return this._sendCommand(type, fields, { roundVersion: this._roundVersion });
  }

  /** 将连续 JPEG 帧显示到传入的 img 元素；预览频率受 Agent 配置上限限制。 */
  async startPreview(imageElement, options = {}) {
    if (!imageElement || typeof imageElement !== 'object') {
      throw new TypeError('startPreview requires an image element.');
    }

    this._previewElement = imageElement;
    try {
      return await this._sendCommand('preview.start', { fps: options.fps });
    } catch (error) {
      if (this._previewElement === imageElement) this._previewElement = null;
      throw error;
    }
  }

  async stopPreview() {
    const data = await this._sendCommand('preview.stop');
    this._clearPreview();
    return data;
  }

  /** 手动抓拍，返回含纯 Base64、尺寸和采集时间的照片对象；自动模式需先切回手动。 */
  async capture() {
    return this._sendCommand('capture');
  }

  /** 关闭摄像头并清理预览，保留 WebSocket 连接以便再次打开设备。 */
  async close() {
    this._invalidateRound();
    const data = await this._sendCommand('camera.close');
    this._clearPreview();
    return data;
  }

  /** 断开连接，清理心跳、预览资源及所有等待中的请求。 */
  disconnect() {
    this._invalidateRound();
    const socket = this._socket;
    this._socket = null;
    this._stopHeartbeat();
    this._clearPreview();
    this._rejectPending(new FaceCaptureError(
      'DISCONNECTED',
      'Disconnected from the local face capture agent.',
      true,
    ));

    if (socket && socket.readyState < 2) {
      socket.close(1000, 'Client disconnected.');
    }
  }

  async _connectWithRetries() {
    let lastError;
    const attempts = this._retryDelays.length + 1;
    for (let attempt = 0; attempt < attempts; attempt += 1) {
      if (attempt > 0) await delay(this._retryDelays[attempt - 1]);
      try {
        await this._connectOnce();
        this._startHeartbeat();
        return;
      } catch (error) {
        lastError = error;
      }
    }

    throw lastError ?? new FaceCaptureError(
      'CONNECTION_FAILED',
      'Could not connect to the local face capture agent.',
      true,
    );
  }

  _connectOnce() {
    return new Promise((resolve, reject) => {
      let socket;
      try {
        socket = this._webSocketFactory(this.url, REQUIRED_PROTOCOL);
      } catch (error) {
        reject(new FaceCaptureError('CONNECTION_FAILED', error.message, true));
        return;
      }

      this._socket = socket;
      socket.binaryType = 'blob';
      let settled = false;
      const timer = setTimeout(() => {
        if (settled) return;
        settled = true;
        if (this._socket === socket) this._socket = null;
        if (socket.readyState < 2) socket.close(1000, 'Connection timed out.');
        reject(new FaceCaptureError(
          'CONNECTION_TIMEOUT',
          'Timed out connecting to the local face capture agent.',
          true,
        ));
      }, this._connectTimeoutMs);

      socket.onopen = () => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        resolve();
      };
      socket.onmessage = event => {
        if (this._socket === socket) this._handleMessage(event.data);
      };
      socket.onerror = () => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        if (this._socket === socket) this._socket = null;
        if (socket.readyState < 2) socket.close(1000, 'Connection failed.');
        reject(new FaceCaptureError(
          'CONNECTION_FAILED',
          'Could not connect to the local face capture agent.',
          true,
        ));
      };
      socket.onclose = () => {
        if (!settled) {
          settled = true;
          clearTimeout(timer);
          if (this._socket === socket) this._socket = null;
          reject(new FaceCaptureError(
            'CONNECTION_FAILED',
            'The local face capture agent closed the connection.',
            true,
          ));
          return;
        }

        if (this._socket === socket) {
          this._handleSocketClosed(socket);
        }
      };
    });
  }

  _sendCommand(type, fields = {}, options = {}) {
    const socket = this._socket;
    if (!socket || socket.readyState !== 1) {
      return Promise.reject(new FaceCaptureError(
        'NOT_CONNECTED',
        'Connect to the local face capture agent first.',
        true,
      ));
    }

    const requestId = this._nextRequestId();
    const message = removeUndefined({ ...fields, type, requestId });
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        if (!this._pending.delete(requestId)) return;
        const error = new FaceCaptureError(
          'COMMAND_TIMEOUT',
          `${type} did not complete within ${this._commandTimeoutMs} ms.`,
          true,
        );
        reject(error);
        if (options.closeOnTimeout) {
          if (socket.readyState < 2) socket.close(1011, 'Heartbeat timed out.');
          if (this._socket === socket) this._handleSocketClosed(socket, error);
        }
      }, this._commandTimeoutMs);

      this._pending.set(requestId, { resolve, reject, timer, roundVersion: options.roundVersion });
      try {
        socket.send(JSON.stringify(message));
      } catch (error) {
        clearTimeout(timer);
        this._pending.delete(requestId);
        reject(new FaceCaptureError('SEND_FAILED', error.message, true));
      }
    });
  }

  // 同一连接承载三类数据：二进制预览、带 requestId 的响应、带 roundId 的主动事件。
  _handleMessage(data) {
    if (typeof data !== 'string') {
      this._renderPreview(data);
      return;
    }

    let message;
    try {
      message = JSON.parse(data);
    } catch {
      return;
    }

    if (!message || typeof message !== 'object') return;
    if (message.event === true || message.type?.startsWith('auto.')) {
      if (message.event !== true || !this._roundId || message.data?.roundId !== this._roundId) return;
      for (const callback of [...(this._listeners.get(message.type) ?? [])]) {
        try { callback(message.data); } catch { /* Consumer errors cannot break protocol processing. */ }
      }
      return;
    }
    if (typeof message.requestId !== 'string') return;
    const pending = this._pending.get(message.requestId);
    if (!pending) return;
    this._pending.delete(message.requestId);
    clearTimeout(pending.timer);

    if (message.type === 'error') {
      pending.reject(new FaceCaptureError(
        message.error?.code ?? 'UNKNOWN_ERROR',
        message.error?.message ?? 'The local face capture agent returned an error.',
        message.error?.retryable ?? false,
      ));
      return;
    }

    // 必须在 resolve 前同步绑定轮次，紧随响应到达的事件才能立即通过轮次校验。
    if (pending.roundVersion !== undefined && pending.roundVersion === this._roundVersion) {
      this._roundId = typeof message.data?.roundId === 'string' ? message.data.roundId : null;
    }
    pending.resolve(message.data ?? {});
  }

  _renderPreview(data) {
    const image = this._previewElement;
    if (!image || !this._urlApi?.createObjectURL) return;

    const blob = data instanceof Blob ? data : new Blob([data], { type: 'image/jpeg' });
    const previousUrl = this._previewUrl;
    const nextUrl = this._urlApi.createObjectURL(blob);
    if (previousUrl) this._stalePreviewUrls.add(previousUrl);
    this._previewUrl = nextUrl;
    // 等新帧加载后再回收旧 URL，避免浏览器仍在解码时提前释放；停止预览时统一兜底清理。
    const onload = () => {
      for (const staleUrl of this._stalePreviewUrls) {
        this._urlApi.revokeObjectURL(staleUrl);
      }
      this._stalePreviewUrls.clear();
      if (image.onload === onload) image.onload = null;
    };
    image.onload = onload;
    image.src = nextUrl;
  }

  _clearPreview() {
    for (const staleUrl of this._stalePreviewUrls) {
      this._urlApi?.revokeObjectURL?.(staleUrl);
    }
    this._stalePreviewUrls.clear();
    if (this._previewUrl && this._urlApi?.revokeObjectURL) {
      this._urlApi.revokeObjectURL(this._previewUrl);
    }
    this._previewUrl = null;
    this._previewElement = null;
  }

  _startHeartbeat() {
    this._stopHeartbeat();
    this._heartbeatTimer = setInterval(() => {
      if (!this.connected || this._heartbeatPending) return;
      this._heartbeatPending = true;
      this._sendCommand('ping', {}, { closeOnTimeout: true })
        .catch(() => {})
        .finally(() => {
          this._heartbeatPending = false;
        });
    }, this._heartbeatIntervalMs);
    this._heartbeatTimer.unref?.();
  }

  _stopHeartbeat() {
    if (this._heartbeatTimer) clearInterval(this._heartbeatTimer);
    this._heartbeatTimer = null;
    this._heartbeatPending = false;
  }

  _handleSocketClosed(socket, reason) {
    this._invalidateRound();
    if (this._socket === socket) this._socket = null;
    this._stopHeartbeat();
    this._clearPreview();
    this._rejectPending(reason ?? new FaceCaptureError(
      'DISCONNECTED',
      'The local face capture agent disconnected.',
      true,
    ));
  }

  _rejectPending(error) {
    for (const pending of this._pending.values()) {
      clearTimeout(pending.timer);
      pending.reject(error);
    }
    this._pending.clear();
  }

  _nextRequestId() {
    if (globalThis.crypto?.randomUUID) return globalThis.crypto.randomUUID();
    this._counter += 1;
    return `${Date.now()}-${this._counter}`;
  }
}

function removeUndefined(value) {
  return Object.fromEntries(Object.entries(value).filter(([, item]) => item !== undefined));
}

function delay(milliseconds) {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}
