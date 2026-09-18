# FaceCaptureAgent — Windows / 麒麟 Linux

这是一个运行在用户电脑上的本机人脸采集程序。HTTP 业务页面通过
`ws://127.0.0.1:17653/face` 调用普通 USB/UVC RGB 摄像头，显示 JPEG 预览并抓拍一张照片；抓拍结果以不带 Data URI 前缀的 Base64 返回。业务页面再把它提交给已有的人脸 1:1 比对 API。

> **安全提示：** 按当前已确认的需求，本程序不校验令牌，也不校验 WebSocket `Origin`。任何能在这台电脑浏览器中运行的网页都可以尝试调用摄像头并取得抓拍照片。程序只监听 `127.0.0.1`，但这不能阻止本机恶意网页调用。正式部署前应再次确认是否接受这一风险。

项目已采用共享核心与独立平台宿主：`FaceCaptureAgent.Core` 负责协议、会话、检测和内置测试页，`FaceCaptureAgent` 保留 Windows Forms 桌面，`FaceCaptureAgent.Linux` 提供 GTK 3 桌面和 V4L2 摄像头后端。历史目录名 `windows-poc` 保留。网页 SDK 和现有业务接口保持兼容，`system.info` 按运行环境返回平台、操作系统和架构。

Windows x64 已完成回归、发布冒烟检查和安装包构建，详见 [2026-09-19 打包记录](docs/windows-package-2026-09-19.md)。Linux 支持 ARM64 / x64 构建配置，但尚未完成麒麟实机验收。目标银河麒麟 V10 SP1 2403 ARM64 使用 glibc 2.31，而当前官方 ARM64 OpenCV 原生包要求 glibc 2.38，因此发布必须提供兼容的自编译原生库，不能直接交付官方包。步骤见 [麒麟部署文档](docs/kylin-deployment.md)和[原生库构建说明](docs/kylin-native-build.md)。

## 环境与构建

要求：

- Windows x64；
- .NET 10 SDK（开发和构建）；
- Node.js（运行 SDK 测试和演示静态服务）；
- 普通 UVC 摄像头。

在 `windows-poc` 目录执行：

```powershell
dotnet restore FaceCaptureAgent\FaceCaptureAgent.csproj --configfile NuGet.Config
dotnet restore FaceCaptureAgent.Tests\FaceCaptureAgent.Tests.csproj --configfile NuGet.Config
dotnet test FaceCaptureAgent.Tests\FaceCaptureAgent.Tests.csproj
node --test web-sdk\face-capture.test.mjs
```

## 启动本机程序

开发模式：

```powershell
dotnet run --project FaceCaptureAgent\FaceCaptureAgent.csproj
```

程序默认读取可执行文件同目录的 `config.toml`。也可以显式指定绝对路径：

```powershell
dotnet run --project FaceCaptureAgent\FaceCaptureAgent.csproj -- --config D:\absolute\path\config.toml
```

`--config` 不接受相对路径；配置缺失或不合法时程序拒绝启动。

启动后自动显示“刷脸认证”日志窗口，并在任务栏和系统托盘显示程序图标。
点击最小化后，窗口及任务栏图标隐藏，只保留系统托盘图标；单击托盘图标可恢复窗口及任务栏图标。
单击图标或右键选择“查看日志”，可查看带时间的连接、打开/关闭摄像头、
预览、抓拍和断开连接日志，失败操作显示错误码。日志仅保留本次运行最近
1,000 条，不写入磁盘，不记录照片、Base64 或请求内容。
关闭日志窗口后程序继续在后台运行；右键图标选择“退出”会停止服务、
关闭活动连接、释放摄像头并移除托盘图标。
图标显示在隐藏区域还是直接显示在任务栏，由 Windows 通知区域设置决定。

确认监听范围：

```powershell
Get-NetTCPConnection -LocalPort 17653 | Select-Object LocalAddress,LocalPort,State
```

`LocalAddress` 必须只有 `127.0.0.1`，不得出现 `0.0.0.0`、局域网地址或 `::`。

## 配置参数

| 参数 | 默认值 | 含义 |
|---|---:|---|
| `listen_address` | `127.0.0.1` | 监听地址；程序强制只能为 IPv4 回环地址 `127.0.0.1` |
| `listen_port` | `17653` | WebSocket 端口，合法范围 1024–65535 |
| `camera_index` | `0` | 默认摄像头索引；Windows 枚举 0–9，Linux 枚举实际 `/dev/videoN` 节点 |
| `capture_width` | `1280` | 请求摄像头采用的宽度；最终响应返回实际宽度 |
| `capture_height` | `720` | 请求摄像头采用的高度；最终响应返回实际高度 |
| `preview_fps` | `5` | 浏览器预览帧率上限，合法范围 1–15 |
| `jpeg_quality` | `85` | JPEG 编码质量，合法范围 1–100 |
| `max_image_bytes` | `2097152` | 单张 JPEG 最大字节数，默认 2 MiB |
| `camera_timeout_seconds` | `10` | 打开摄像头或抓拍等待超时秒数 |

配置中没有 `allowed_origins`。服务接受不同网站的 WebSocket Origin，这正是当前方案的行为；网络隔离仅依赖 `127.0.0.1` 监听。

如果预览出现红外灰度图，请在演示页切换 `Camera 0`、`Camera 1` 等索引，找到 RGB 彩色摄像头后把对应索引写入 `camera_index`。

## 启动演示页面

另开一个 PowerShell，在 `windows-poc` 目录执行：

```powershell
.\scripts\run-demo.ps1
```

打开 `http://127.0.0.1:18080/demo/`，依次点击“连接服务”“打开摄像头”“抓拍照片”。演示页不调用远程 API，也不把照片写入磁盘。

## 业务页面接入

另有独立的 [Vue 3 + Vite 示例](vue3-demo/README.md)，直接复用 `web-sdk/face-capture.js`，
提供连接、设备选择、实时预览、抓拍、JPEG 下载和操作日志。在 `vue3-demo` 目录执行
`npm ci`、`npm run dev`，打开 `http://127.0.0.1:5173`。此示例与原 `demo/` 分开维护。

Vue 示例支持“手动 / 自动拍照”配置：自动模式由本机 Agent 检测单张人脸稳定约 1.5 秒后抓拍一次，点击“重新拍照”开始下一轮。模型随 Agent 安装包部署，前端只展示状态和照片；具体规则与部署说明见 Vue 示例 README。

部署 `web-sdk/face-capture.js` 到业务网站，然后使用 ES Module：

```html
<img id="face-preview" alt="摄像头预览">
<script type="module">
  import { FaceCaptureClient } from '/assets/face-capture.js';

  const face = new FaceCaptureClient();
  await face.connect();
  const devices = await face.listDevices();
  await face.open({ deviceId: devices[0].id, width: 1280, height: 720, fps: 15 });
  await face.startPreview(document.querySelector('#face-preview'), { fps: 5 });

  const photo = await face.capture();
  // photo.base64 是纯 Base64，不含 data:image/jpeg;base64, 前缀。
  const verifyResult = await fetch('/api/face/verify-1to1', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ liveImageBase64: photo.base64, referenceId: '业务侧人员标识' }),
  }).then(response => response.json());

  await face.close();
  face.disconnect();
</script>
```

上述 API 路径和字段只是接线示例，实际应替换为现有 1:1 比对接口。API 密钥不得写入浏览器 SDK。若远程接口仍为 HTTP，人脸照片在网络传输中不具备 HTTPS 的机密性和完整性保护。

SDK 方法：`connect`、`getSystemInfo`、`listDevices`、`open`、`startPreview`、`stopPreview`、`capture`、`setCaptureMode`、`rearm`、`on`、`close`、`disconnect`。页面卸载时应调用 `disconnect()`；服务端也会在 WebSocket 断开时释放摄像头。

### 本机自动拍照（Agent 1.1.0）

```js
const face = new FaceCaptureClient();
const unsubscribe = face.on('auto.capture', photo => {
  document.querySelector('#face-photo').src = `data:image/jpeg;base64,${photo.base64}`;
});
face.on('auto.status', ({ status }) => {
  // no-face / multiple-faces / stabilizing / complete，按业务需要显示提示。
});
face.on('auto.error', ({ code, cameraClosed }) => {
  // cameraClosed 为 true 时清理预览并提示重新打开摄像头。
  // 否则可重新检测或切回手动；不要记录照片或原始响应。
});
await face.connect();
await face.open({ deviceId: '0', captureMode: 'auto', stableDurationMs: 1500 });
// 可选：await face.startPreview(document.querySelector('#face-preview'));
// 用户点击“重新拍照”时：await face.rearm();
// 用户切回手动时：await face.setCaptureMode({ captureMode: 'manual' });
// 页面离开时：unsubscribe(); face.disconnect();
```

参数仅影响当前 WebSocket 会话，不修改磁盘配置。省略模式时默认 `manual`；`stableDurationMs` 为 500–10000 的整数，默认 1500。自动模式只推送一次照片，之后等待 `rearm()`；自动模式下显式 `capture()` 返回 `INVALID_STATE`，切回手动后可调用。人脸移动、多人、无人脸和超过 750ms 的检测间隔都会重置计时；这是位置稳定判断，不是活体或身份校验。

新增命令 `camera.setCaptureMode`、`capture.rearm`。打开、切换和重启响应均包含 `roundId`；主动事件格式为 `{ type, event: true, data: { roundId, ... } }`，不含 `requestId`。SDK 只分发当前轮事件，控制请求开始和断线时立即作废旧轮。`system.info` 的 `capabilities` 包含 `auto-capture`；旧 Agent 可继续手动抓拍，但使用自动功能需升级 Agent。

模型采用 [OpenCV Zoo YuNet](https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet)，模型、许可证和校验值在 `FaceCaptureAgent/Models/`。该目录随发布和安装包离线交付，运行时无需外部 CDN。Windows x64 以外的平台仍需单独验证。

## 错误排查

- `CAMERA_BUSY`：另一个网页会话或程序正在占用摄像头；关闭其他会话后重试。
- `CAMERA_OPEN_FAILED`：检查摄像头权限、索引和其他视频软件占用。
- `CAMERA_DISCONNECTED`：摄像头被拔出或未返回有效图像。
- `CAPTURE_TIMEOUT`：在配置时间内没有取得图像。
- `IMAGE_TOO_LARGE`：JPEG 超出 `max_image_bytes`。
- `FACE_DETECTION_FAILED`：本机检测模型缺失或推理失败；检查安装目录 Models，重试或切回手动。
- 无法连接：先确认 `FaceCaptureAgent` 正在运行，再检查端口 17653。

同一时刻只有一个 WebSocket 会话可以拥有摄像头。刷新或关闭页面后，服务端通常会立即释放设备；若异常页面未断开，可关闭对应浏览器标签或重启本机程序。

## 发布 Windows x64 自包含版本

```powershell
.\scripts\publish-win-x64.ps1
```

输出目录为 `publish/windows-x64`。将整个目录交付到 Windows x64 电脑，运行：

```powershell
.\publish\windows-x64\FaceCaptureAgent.exe
```

发布目录中的 `config.toml` 和 `Models/` 必须与可执行文件一起交付。程序不会存储预览帧、JPEG 或 Base64；请勿在业务页面日志中打印完整 Base64。

## 构建后台自启动安装包

要求本机已安装 Inno Setup 6。在仓库根目录执行：

```powershell
.\scripts\build-installer.ps1
```

也可以指定版本或编译器路径：

```powershell
.\scripts\build-installer.ps1 -Version 1.1.3 `
  -IsccPath 'D:\appInstall\Inno Setup 6\ISCC.exe'
```

输出为 `installer-output/刷脸认证.exe`。安装包是 Windows x64 当前用户安装程序，不要求管理员权限，也不要求目标电脑预装 .NET。程序默认安装到 `%LocalAppData%\Programs\FaceCaptureAgent`，安装完成后自动在当前用户桌面创建“刷脸认证”快捷方式，可双击启动程序；同时立即启动并显示日志窗口，并通过 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 在用户登录时自动启动。程序没有控制台窗口，仍只监听 `127.0.0.1:17653`。覆盖安装或升级不会改写已有的 `config.toml`。

可在“设置 → 应用 → 已安装的应用”中卸载。安装包会在卸载时终止后台进程，并删除桌面快捷方式和登录自启动项。需要做完整安装冒烟验证时执行：

```powershell
.\scripts\test-installer.ps1
```

该脚本会临时安装、执行覆盖升级、验证配置保留、后台进程、回环监听和路径限定卸载，然后自动清理。运行前不能有其他 `FaceCaptureAgent.exe` 进程、同名自启动项或已注册的 FaceCaptureAgent 安装。发布目录清理可单独验证：

```powershell
.\scripts\test-publish-cleanup.ps1
```

## Vue 3 可复用采集组件

`face-capture-widget/` 提供独立组件包 `face-capture-vue`，业务项目需要 Vue 3.5+，并在用户电脑运行本机 Agent。包包含两个入口：

- `FaceCapture` 负责取景框、实时预览、连接动画与自动抓拍；调用方自定义弹窗、按钮和提示，通过 `active` 控制采集，通过事件接收照片、连接失败和倒计时，通过 `retake()` / `cancel()` 控制当前会话。成功或连接超时后由调用方决定关闭页面。
- `FaceCaptureDialog` 提供默认弹窗，兼容 `v-model` 和 `result` 接口，成功或连接超时后自动关闭。

默认连接超时为 60 秒，连接成功后停止倒计时。关闭采集会清理连接与预览资源；照片返回给调用方，不自动上传。现有 `vue3-demo` 和内置测试页保持独立。

在仓库根目录执行以下命令开发和打包：

```powershell
npm --prefix face-capture-widget ci
npm --prefix face-capture-widget run dev
npm --prefix face-capture-widget test
npm --prefix face-capture-widget run typecheck
npm --prefix face-capture-widget pack
```

示例地址为 `http://127.0.0.1:5175`，包含默认弹窗和调用方自定义面板。当前尚未发布 npm，可将生成的 `.tgz` 安装到业务项目。接入方式、属性、事件和生命周期见[组件文档](face-capture-widget/README.md)，验证范围见[验证记录](face-capture-widget/VALIDATION.md)。

## 内置测试页

启动采集程序后，在运行日志窗口点击“打开测试页”，默认浏览器会打开本机测试页面。页面和全部前端资源已嵌入程序，安装后的电脑无需 Node.js、源码目录或另外启动测试项目。

默认地址为 `http://127.0.0.1:17653/test/`。修改 `config.toml` 的 `listen_port` 后，按钮和内置页面的默认 WebSocket 地址都会使用该端口。服务启动完成前按钮不可用；浏览器启动失败时会显示可手动访问的地址。退出采集程序后测试页服务随之停止。

源码构建现在需要 Node.js 22.12.0 或更高版本：.NET 构建会自动执行 Vue 生产构建并嵌入资源，首次缺少 node_modules 时自动执行 npm ci。已有依赖时，变更 package-lock.json 后应先执行 `npm --prefix vue3-demo ci`。独立 Vue 开发模式仍默认连接 17653 端口。
## 独立动作校验

自动模式用 `verificationAction` 指定本轮唯一动作，默认 `none`。已移除旧布尔开关，调用方需更新参数。

| 参数值 | 本轮通过条件 |
| --- | --- |
| `none` | 单人脸稳定后拍照 |
| `blink` | 双眼睁开 → 闭合 → 重新睁开 → 稳定抓拍 |
| `mouth-open` | 正脸闭嘴 → 张嘴 → 闭嘴 → 稳定抓拍 |
| `turn-left` | 正脸 → 向本人左侧转头 → 回正 → 稳定抓拍 |
| `turn-right` | 正脸 → 向本人右侧转头 → 回正 → 稳定抓拍 |

```js
await client.open({ captureMode: 'auto', verificationAction: 'mouth-open', stableDurationMs: 1500 });
await client.rearm(); // 使用同一动作重新校验
await client.setCaptureMode({ captureMode: 'auto', verificationAction: 'turn-left' }); // 新动作、新轮次
```

`camera.open`、`camera.setCaptureMode`、`capture.rearm` 回显 `verificationAction`。`system.info` 提供 `verification-action` 能力及 `verificationActions` 列表。SDK 在绑定 roundId 前核对动作，缺少确认或返回另一动作会报 `ACTION_UNSUPPORTED`，不接受该轮照片。参数仅作用于自动模式，手动模式仍可直接拍照。未指定动作的模式切换沿用当前动作；新开摄像头默认 `none`。

Vue 调用方在开始一轮之前，从四种动作中随机选一个传入；Agent 不做随机选择，也不组合其他动作。`face-capture-widget/examples/Example.vue` 展示随机调用，内置 `/test/` 页面提供动作下拉选择。预览镜像不改变左右定义：按使用者本人的左右提示。

除眨眼原有状态外，`auto.status` 包含 `mouth-open-required`、`mouth-close-required`、`turn-left-required`、`turn-right-required`、`action-face-camera`、`action-return`、`action-passed`。每轮动作校验与稳定抓拍最多 15 秒，超时报 `ACTION_TIMEOUT`，需显式重试。无人脸、多人、关键点无效、明显位置/尺寸变化或超过 350ms 的帧间隔会重置动作进度。

检测在本机完成，复用 YuNet 和 Face Mesh 离线模型。张嘴用唇间距/嘴宽，转头用鼻尖相对双眼轴的归一化偏移（不是角度估计）。新动作的初始、动作和恢复阶段均要求连续 200ms，返回自然姿态后才开始稳定计时。转头不依赖嘴部或眨眼状态，张嘴不依赖眨眼状态。

这属于动作校验，不保证防照片/视频回放，也不做身份连续性识别。新动作的阈值及本人左右方向仍需真人摄像头验证，覆盖距离、光照、眼镜和目标设备性能；模拟帧测试不能代替现场验收。

## 手动生成 Windows 安装包

打包机需安装符合 `global.json` 的 .NET SDK（10.0.400，允许向更高特性带滚动）、Node.js 22.12+ 和 Inno Setup 6。在本目录运行：

```powershell
npm --prefix vue3-demo ci
.\scripts\build-installer.ps1
# 未自动找到 Inno Setup 时指定实际路径：
.\scripts\build-installer.ps1 -IsccPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

脚本依次恢复依赖、运行 Release 测试、构建并嵌入 Vue 页面、发布 Windows x64 自包含程序并编译安装包。输出为 `installer-output/刷脸认证.exe`，目标电脑无需安装 .NET 或 Node.js。

可传 `-Version 1.1.4` 指定安装包版本；此参数只修改安装包元数据，不同步修改 Agent 项目版本或 `system.info.agentVersion`。当前默认版本仍为 `1.1.3`。此次动作版安装包于 2026-09-18 构建成功，但未执行安装或升级验收。

完整功能、测试结果和验收边界见[独立动作校验交付记录](docs/action-verification-validation.md)。
