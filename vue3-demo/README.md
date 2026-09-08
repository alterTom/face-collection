# Vue 3 人脸抓拍 Demo

独立的 Vue 3 + Vite 示例。直接引用仓库中的 `../web-sdk/face-capture.js`，通过 WebSocket 调用本机 `FaceCaptureAgent.exe`，由本机程序访问摄像头；不使用浏览器 `getUserMedia`。原有 `../demo/` 无需修改。

## 启动

1. 在 Windows x64 电脑安装并运行 FaceCaptureAgent，确认托盘图标存在。
2. 安装 Node.js 22.12 或更新版本。在本目录执行：

```powershell
npm ci
npm run dev
```

打开 `http://127.0.0.1:5173`。依次点击“连接服务” → 选择摄像头 → “打开摄像头” → “抓拍照片”。打开摄像头时自动开始预览，抓拍后可查看尺寸和大小，并点击“下载 JPEG”。默认服务地址是 `ws://127.0.0.1:17653/face`，可以修改本机端口。

“关闭摄像头”保留服务连接，“断开连接”释放整个会话。页面离开或组件卸载时也会断开。操作失败会显示中文说明；摄像头操作失败时主动断开以恢复一致状态，可以重新连接后重试。

如果未找到设备，接入 USB 摄像头后点击“刷新设备”。刷新或切换设备前需先关闭摄像头。原演示页或其他程序占用摄像头时，请先释放设备。

## 手动与自动拍照

在“采集控制 → 拍照模式”选择模式，页面默认使用手动拍照；选择只在本次页面中生效。

- **手动拍照**：点击“抓拍照片”取得一张照片。
- **自动拍照**：本机 Agent 检测单张人脸，位置、大小连续稳定约 1.5 秒后推送一次照片，浏览器只展示状态与结果。
- 拍完显示“拍照完成”，点击“重新拍照”通过 `rearm()` 开始下一轮；检测期间可点击“重新检测”重置。
- 无人脸、多人、明显移动或超过 750 毫秒的帧间隔都会重新计时。切换模式、关闭摄像头、断开连接会取消当前轮；SDK 丢弃旧轮事件。
- 检测失败可以重新检测或切回手动。摄像头故障需重新打开设备。

自动检测在 Agent 内离线运行，不依赖浏览器预览或页面可见性；本机 `config.toml` 不控制拍照模式。模式与稳定时长仅影响当前会话。浏览器不再加载检测 Worker、WASM 或模型；YuNet 模型随 Agent 安装包提供。

“稳定”是拍照触发条件，不包含清晰度、正脸角度、身份比对或活体检测。真实设备上的光照、距离、性能仍需实测。

## SDK 调用示例

```js
import { FaceCaptureClient } from '../../web-sdk/face-capture.js';

const client = new FaceCaptureClient();
await client.connect();
const devices = await client.listDevices();
await client.open({ deviceId: devices[0].id, width: 1280, height: 720 });
await client.startPreview(previewImageElement, { fps: 5 });
const photo = await client.capture();
// photo.base64 为不带 Data URI 前缀的 Base64，可交给业务方的上传逻辑。
// 本 demo 不上传照片，也不输出 Base64 到日志。
await client.close();
client.disconnect();
```

## 构建与测试

```powershell
npm test
npm run build
npm run preview
```

自动化测试使用合成 JPEG 和本机模拟 WebSocket 服务，不会访问真实摄像头。
需要手工验证页面时，可在另一个终端执行 `node tests/fixtures/agent.mjs`，
把页面服务地址改为 `ws://127.0.0.1:17654/face`。该服务仅返回标有
`TEST FRAME` 的测试图片；验证真实硬件时改回 `ws://127.0.0.1:17653/face`。

预览生产构建：`http://127.0.0.1:4173`。`dist/` 已打包 Vue、SDK 与图标，可放到静态 HTTP 服务下；不要通过 `file://` 双击 HTML。源码运行与构建需保留同级 `web-sdk/`、`FaceCaptureAgent/Assets/`，不能只复制 `vue3-demo/` 源码目录。

本示例面向本机 HTTP 演示。部署到 HTTPS 网站时，需另行处理浏览器对本机 `ws://` 混合内容及本地网络访问的限制，不能保证直接连接；不要通过关闭浏览器安全设置规避。

## 文件职责

- `src/App.vue`：页面组合与组件卸载清理。
- `src/capture-controller.js`：SDK 调用、状态管理、错误提示和日志。
- `src/components/PreviewPanel.vue`：实时预览和抓拍按钮。
- `src/components/CaptureResult.vue`：照片展示与下载。
- `src/style.css`：蓝白界面及移动端布局。
- `tests/`：成功、失败、断线和异步结果失效测试。

照片仅存于当前页面内存，点击下载才保存文件；刷新页面会丢失抓拍结果。日志最多 100 条，避免输出照片、Base64 和未经处理的服务端错误内容。模拟服务仅使用无个人数据的合成画面，并按预定时序发送自动事件，用于验证协议、界面和重新拍照；它不执行人脸检测，不能替代真实人脸与摄像头验收。

## 自动模式 SDK 示例

```js
const unsubscribe = client.on('auto.capture', photo => { /* 展示 photo.base64 */ });
client.on('auto.status', data => { /* data.status: no-face / multiple-faces / stabilizing / complete */ });
client.on('auto.error', data => { /* 按 data.code 显示安全提示；cameraClosed 表示设备已关闭 */ });
await client.open({ deviceId: '0', captureMode: 'auto', stableDurationMs: 1500 });
await client.setCaptureMode({ captureMode: 'auto', stableDurationMs: 2000 });
await client.rearm();
unsubscribe();
```

`stableDurationMs` 为 500–10000 的整数，默认 1500；`captureMode` 默认 `manual`。SDK 在成功响应后同步绑定 `roundId`，调用切换模式、重新拍照、关闭、断开时立即使旧轮失效。自动事件不能完成待处理命令。原有手动调用保持兼容；自动功能需要新版 Agent。
