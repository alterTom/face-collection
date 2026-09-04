const DEFAULT_URL = 'ws://127.0.0.1:17653/face';
const REQUIRED_PROTOCOL = 'face-capture.v1';

export class FaceCaptureError extends Error {
  constructor(code, message, retryable = false) {
    super(message);
    this.name = 'FaceCaptureError';
    this.code = code;
    this.retryable = Boolean(retryable);
  }
}

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
    this._pending = new Map();
    this._counter = 0;
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

  async open(options = {}) {
    return this._sendCommand('camera.open', {
      deviceId: options.deviceId,
      width: options.width,
      height: options.height,
      fps: options.fps,
    });
  }

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

  async capture() {
    return this._sendCommand('capture');
  }

  async close() {
    const data = await this._sendCommand('camera.close');
    this._clearPreview();
    return data;
  }

  disconnect() {
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
      socket.onmessage = event => this._handleMessage(event.data);
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

      this._pending.set(requestId, { resolve, reject, timer });
      try {
        socket.send(JSON.stringify(message));
      } catch (error) {
        clearTimeout(timer);
        this._pending.delete(requestId);
        reject(new FaceCaptureError('SEND_FAILED', error.message, true));
      }
    });
  }

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
