import { FaceCaptureClient, FaceCaptureError } from '../web-sdk/face-capture.js';

const face = new FaceCaptureClient();
const elements = {
  connectionState: document.querySelector('#connectionState'),
  connectionLabel: document.querySelector('#connectionLabel'),
  cameraSelect: document.querySelector('#cameraSelect'),
  connectButton: document.querySelector('#connectButton'),
  openButton: document.querySelector('#openButton'),
  captureButton: document.querySelector('#captureButton'),
  retakeButton: document.querySelector('#retakeButton'),
  closeButton: document.querySelector('#closeButton'),
  copyButton: document.querySelector('#copyButton'),
  previewStage: document.querySelector('#previewStage'),
  previewImage: document.querySelector('#previewImage'),
  captureFrame: document.querySelector('#captureFrame'),
  capturedImage: document.querySelector('#capturedImage'),
  statusMessage: document.querySelector('#statusMessage'),
  imageDimensions: document.querySelector('#imageDimensions'),
  imageSize: document.querySelector('#imageSize'),
  base64Length: document.querySelector('#base64Length'),
};

let rawBase64 = '';
let cameraOpen = false;

elements.connectButton.addEventListener('click', connect);
elements.openButton.addEventListener('click', openCamera);
elements.captureButton.addEventListener('click', capturePhoto);
elements.retakeButton.addEventListener('click', clearCapture);
elements.closeButton.addEventListener('click', closeCamera);
elements.copyButton.addEventListener('click', copyBase64);
window.addEventListener('pagehide', () => face.disconnect());

async function connect() {
  setBusy(elements.connectButton, true);
  setStatus('正在连接本机采集服务…');
  try {
    await face.connect();
    const [info, devices] = await Promise.all([face.getSystemInfo(), face.listDevices()]);
    fillDevices(devices);
    elements.connectionState.dataset.state = 'online';
    elements.connectionLabel.textContent = `已连接 · v${info.agentVersion ?? '1.0.0'}`;
    elements.connectButton.disabled = true;
    elements.openButton.disabled = devices.length === 0;
    setStatus(
      devices.length > 0 ? `本机服务已连接，发现 ${devices.length} 个摄像头` : '本机服务已连接，但没有发现可用摄像头',
      devices.length > 0 ? 'success' : 'error',
    );
  } catch (error) {
    face.disconnect();
    elements.connectionState.dataset.state = 'error';
    elements.connectionLabel.textContent = '连接失败';
    setStatus('无法连接本机采集服务。请先启动 FaceCaptureAgent，再重新连接。', 'error');
  } finally {
    setBusy(elements.connectButton, false);
  }
}

async function openCamera() {
  if (!elements.cameraSelect.value) return;
  setBusy(elements.openButton, true);
  setStatus('正在打开摄像头…');
  try {
    const result = await face.open({
      deviceId: elements.cameraSelect.value,
      width: 1280,
      height: 720,
      fps: 15,
    });
    await face.startPreview(elements.previewImage, { fps: 5 });
    cameraOpen = true;
    elements.previewStage.dataset.active = 'true';
    elements.cameraSelect.disabled = true;
    elements.captureButton.disabled = false;
    elements.closeButton.disabled = false;
    elements.openButton.disabled = true;
    setStatus(`摄像头已打开：${result.width} × ${result.height}，可以抓拍`, 'success');
  } catch (error) {
    setStatus(readableError(error), 'error');
  } finally {
    setBusy(elements.openButton, false);
  }
}

async function capturePhoto() {
  setBusy(elements.captureButton, true);
  setStatus('正在抓拍清晰照片…');
  try {
    const photo = await face.capture();
    rawBase64 = photo.base64;
    elements.capturedImage.src = `data:image/jpeg;base64,${rawBase64}`;
    elements.captureFrame.dataset.active = 'true';
    elements.imageDimensions.textContent = `${photo.width} × ${photo.height}`;
    elements.imageSize.textContent = formatBytes(photo.size);
    elements.base64Length.textContent = rawBase64.length.toLocaleString('zh-CN');
    elements.retakeButton.disabled = false;
    elements.copyButton.disabled = false;
    setStatus('抓拍完成。页面已取得纯 Base64 数据。', 'success');
  } catch (error) {
    setStatus(readableError(error), 'error');
  } finally {
    setBusy(elements.captureButton, false);
  }
}

function clearCapture() {
  rawBase64 = '';
  elements.capturedImage.removeAttribute('src');
  delete elements.captureFrame.dataset.active;
  elements.imageDimensions.textContent = '—';
  elements.imageSize.textContent = '—';
  elements.base64Length.textContent = '—';
  elements.retakeButton.disabled = true;
  elements.copyButton.disabled = true;
  setStatus(cameraOpen ? '已清除抓拍结果，可以重新抓拍' : '抓拍结果已清除');
}

async function closeCamera() {
  setBusy(elements.closeButton, true);
  try {
    await face.close();
    cameraOpen = false;
    delete elements.previewStage.dataset.active;
    elements.previewImage.removeAttribute('src');
    elements.cameraSelect.disabled = false;
    elements.openButton.disabled = false;
    elements.captureButton.disabled = true;
    elements.closeButton.disabled = true;
    setStatus('摄像头已关闭，本机服务仍保持连接', 'success');
  } catch (error) {
    setStatus(readableError(error), 'error');
  } finally {
    setBusy(elements.closeButton, false);
  }
}

async function copyBase64() {
  if (!rawBase64) return;
  try {
    await navigator.clipboard.writeText(rawBase64);
    setStatus('Base64 已复制到剪贴板', 'success');
  } catch {
    setStatus('浏览器未允许写入剪贴板，请在业务代码中直接读取抓拍结果。', 'error');
  }
}

function fillDevices(devices) {
  elements.cameraSelect.replaceChildren();
  if (devices.length === 0) {
    elements.cameraSelect.add(new Option('没有发现可用摄像头', ''));
    elements.cameraSelect.disabled = true;
    return;
  }

  for (const device of devices) {
    elements.cameraSelect.add(new Option(device.name, device.id));
  }
  elements.cameraSelect.disabled = false;
}

function setBusy(button, busy) {
  button.dataset.busy = String(busy);
  if (busy) button.disabled = true;
  else if (button === elements.connectButton) button.disabled = face.connected;
  else if (button === elements.openButton) button.disabled = cameraOpen || !face.connected;
  else if (button === elements.captureButton) button.disabled = !cameraOpen;
  else if (button === elements.closeButton) button.disabled = !cameraOpen;
}

function setStatus(message, tone = 'info') {
  elements.statusMessage.dataset.tone = tone;
  elements.statusMessage.querySelector('span').textContent = message;
}

function readableError(error) {
  if (!(error instanceof FaceCaptureError)) return '本机采集发生未知错误，请重试。';
  const messages = {
    CAMERA_BUSY: '摄像头正在被另一个页面或程序使用，请关闭后重试。',
    NO_CAMERA: '没有发现可用摄像头。',
    CAMERA_OPEN_FAILED: '摄像头打开失败，请检查设备占用和权限。',
    CAMERA_DISCONNECTED: '摄像头已断开，请重新连接设备。',
    CAPTURE_TIMEOUT: '等待摄像头图像超时，请重试。',
    IMAGE_TOO_LARGE: '抓拍图片超过驱动配置的大小上限。',
  };
  return messages[error.code] ?? error.message;
}

function formatBytes(bytes) {
  if (!Number.isFinite(bytes)) return '—';
  if (bytes < 1024) return `${bytes} B`;
  return `${(bytes / 1024).toFixed(1)} KiB`;
}
