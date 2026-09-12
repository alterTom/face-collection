# Repository Guidelines

## 项目结构与模块组织

`FaceCaptureAgent/` 是 .NET 10 本机服务，按 `Camera/`、`Configuration/`、`Protocol/` 和 `Sessions/` 划分职责。`FaceCaptureAgent.Tests/` 使用相同目录结构保存对应测试。`web-sdk/` 包含浏览器 ES Module SDK 及 Node 测试，`demo/` 是手工联调页面，`installer/` 保存 Inno Setup 定义，`scripts/` 提供演示、发布和安装包脚本。不要提交 `bin/`、`obj/`、`publish/`、`installer-output/` 或 `TestResults/` 中的生成物。

## 构建、测试与开发命令

Windows 程序目标为 `net10.0-windows`，以 `WinExe` 运行。`Desktop/` 管理 Windows Forms 托盘与日志窗口，`Diagnostics/` 保存线程安全的内存日志，`Assets/` 保存 PNG 源图、ICO 和生成记录。图标资源属于源码，应提交；安装包和发布目录属于生成物，不应提交。

在仓库根目录使用 PowerShell：

```powershell
dotnet restore FaceCaptureAgent\FaceCaptureAgent.csproj --configfile NuGet.Config
dotnet restore FaceCaptureAgent.Tests\FaceCaptureAgent.Tests.csproj --configfile NuGet.Config
dotnet test FaceCaptureAgent.Tests\FaceCaptureAgent.Tests.csproj
node --test web-sdk\face-capture.test.mjs
dotnet run --project FaceCaptureAgent\FaceCaptureAgent.csproj
.\scripts\run-demo.ps1
.\scripts\publish-win-x64.ps1
.\scripts\build-installer.ps1
.\scripts\convert-icon.ps1
.\scripts\test-publish-cleanup.ps1
.\scripts\test-installer.ps1
```

前两项恢复依赖，`dotnet test` 验证 .NET 服务；Node 命令验证浏览器 SDK；`dotnet run` 启动本机服务；演示脚本在 `http://127.0.0.1:18080/demo/` 提供页面；发布脚本生成 Windows x64 自包含产物。

## Vue 3 独立演示

`vue3-demo/` 是独立的 Vue 3 + Vite 应用，直接复用 `web-sdk/face-capture.js`，不复制 SDK，也不修改原有 `demo/` 来实现新演示。源码开发需要保留同级 SDK 和 `FaceCaptureAgent/Assets/`；构建后的 `dist/` 可由静态 HTTP 服务独立提供。不要提交 `node_modules/`、`dist/` 或本地配置 `*.local`，应提交 `package-lock.json`。

使用 Node.js 22.12.0 或更高版本，在仓库根目录执行：

```powershell
npm --prefix vue3-demo ci
npm --prefix vue3-demo run dev
npm --prefix vue3-demo test
npm --prefix vue3-demo run build
npm --prefix vue3-demo run preview
```

开发地址为 `http://127.0.0.1:5173`，构建预览地址为 `http://127.0.0.1:4173`。先启动本机 Agent，默认连接 `ws://127.0.0.1:17653/face`；此 HTTP 本机演示不代表已解决 HTTPS 页面连接本机 WebSocket 的限制。

Vue 组件使用 Composition API 和 `<script setup>`，连接与摄像头状态集中在 `src/capture-controller.js`。保持断开连接后的异步结果失效保护，页面退出时释放连接和预览资源。照片仅保存在内存并由用户主动下载；页面日志最多保留 100 条，不记录 Base64 或直接展示未经筛选的服务端错误内容。

修改 Vue 演示时，除 .NET 与 SDK 测试外，还须运行上述 npm 测试和构建。`tests/fixtures/agent.mjs` 是测试专用 WebSocket 服务，手工运行时监听 17654 端口；`frame.jpg` 是无个人数据的合成测试帧，可作为测试源码提交。不得用真实人脸照片替换测试帧，也不得将模拟服务接入生产流程。页面交互变更需验证桌面与移动端、连接、预览、抓拍、下载、关闭摄像头、断开及错误恢复；模拟测试和浏览器验证不能替代真实设备验证。

## Vue 3 采集组件包

`face-capture-widget/` 是独立的 Vue 3.5+ 组件包，当前包名 `face-capture-vue`，版本 0.1.0。源码复用同级 `web-sdk/`，构建时打包 SDK、外置 Vue；不依赖或修改 `vue3-demo` 页面。保持现有 Demo 和 Agent 内置 `/test/` 入口独立。

- 公开入口包含 `FaceCapture`（仅取景框）和 `FaceCaptureDialog`（默认弹窗，组合 FaceCapture）。前者由 `active` 控制采集，提供 success、connection-failed、error、cancelled、state-change、countdown、result 事件和 retake/cancel 方法，不负责页面关闭；后者保持 `v-model` / `result` 兼容。类型声明位于 `src/index.d.ts`，调用方须引入 `face-capture-vue/style.css`。
- `FaceCapture` 不包含页面标题、按钮或提示文字。active=false 即使没有卸载也必须清理资源；成功或超时后保持 active=true 不得自动开始下一轮。调用方在自己的页面处理提示和结果，勿同时用 result 与分类事件重复执行上传。连接期间只旋转外圈，内部画面保持静止。
- 弹窗自动连接、枚举设备、预览并使用 Agent 自动抓拍。成功后先关闭弹窗、清理连接，再返回 `success` 和 JPEG Blob、Base64、尺寸等；照片不上传、不下载、不写日志。圆形裁切与镜像只影响预览。
- 默认 60,000 ms 连接截止时间由所有重试共享，连接成功立即取消倒计时；超时关闭并返回 `connection_failed / AGENT_CONNECTION_TIMEOUT`。成功连接后的拍照不受该倒计时限制。
- 「重新拍照」在采集中调用 rearm；设备或检测异常时重新连接并开启新的连接倒计时。连接和打开设备期间禁止重复操作。
- 退出、Escape、调用方关闭、卸载和 pagehide 必须清理定时器、订阅、预览与连接。每次会话最多返回一次结果；保持连接版本与 SDK 轮次失效保护，防止迟到响应恢复旧会话。
- 使用原生 dialog 和 Teleport，样式限定组件作用域，不增加全局 body 或按钮样式。参数在每次打开时读取；不把服务配置或日志放入采集弹窗。
- 提交 package-lock.json；不提交 node_modules、dist、*.tgz、.qa。npm 打包仅包含 dist、README 和包元数据。正式发布前确定 npm 包名、权限和许可；当前未发布。

在仓库根目录执行：

```powershell
npm --prefix face-capture-widget ci
npm --prefix face-capture-widget test
npm --prefix face-capture-widget run typecheck
npm --prefix face-capture-widget run dev
npm --prefix face-capture-widget run test:agent
npm --prefix face-capture-widget pack
```

组件测试先构建并验证实际交付产物。示例端口为 5175，合成测试 Agent 端口为 17658，测试图片复用 `vue3-demo/tests/fixtures/frame.jpg`；测试服务不得接入生产。固定 TypeScript 5.9.3 以兼容当前 vue-tsc。改动需通过组件测试、类型检查和构建，提交前运行既有 SDK 与 .NET 测试；交互变更检查桌面/移动布局、倒计时、自动抓拍返回、取消与错误恢复。

2026-09-12 拆分取景组件后已通过组件 20 项测试、SDK 11 项测试、.NET Release 87 项测试及独立 tarball 安装项目的类型检查和生产构建。浏览器使用合成帧验证默认弹窗及自定义面板，包括桌面/移动布局、预览、重拍、退出、自动抓拍；首次交付还验证了完整 60 秒超时。详情见 `face-capture-widget/VALIDATION.md`。真实摄像头、目标机器性能与正式 HTTPS 环境仍需验收；移动视口验证不代表手机可以连接电脑 localhost。

## 自动拍照（Agent 实现，2026-09-09）

- 人脸检测、稳定判断和自动抓拍位于本机 Agent，会话内独立运行，不依赖浏览器预览。默认手动，参数不写入 config.toml。
- `open({ captureMode: 'auto', stableDurationMs: 1500 })` 开启自动模式；稳定时长为 500–10000 的整数毫秒。`setCaptureMode({ captureMode, stableDurationMs })` 切换；`rearm()` 重新开始一轮。
- `camera.open`、`camera.setCaptureMode`、`capture.rearm` 响应包含 roundId、captureMode、stableDurationMs。WebSocket 先发送响应，再启动自动任务。
- `auto.status`、`auto.capture`、`auto.error` 为带 event:true 和 data.roundId 的独立事件，不使用 requestId。SDK 同步绑定当前轮并丢弃旧轮、旧连接事件。cameraClosed:true 的错误在手动模式和检测停止后仍必须处理。
- 单张人脸连续稳定后只拍一次；无人脸、多人、位置偏移超过锚点宽高 8%、尺寸变化超过 10%、帧间隔超过 750ms 或检测耗时超过 750ms 重置稳定计时。使用单调时钟，与窗口起始位置比较以阻止慢速漂移。
- 自动模式拒绝显式 capture()，先切回手动可继续使用既有抓拍接口。检测失败停止本轮，允许 rearm 或切回手动。
- 模式切换、重新拍照、关闭和退出先取消自动任务再等待命令锁，避免检测阻塞取消；所有循环结束后再释放检测器和摄像头。设备故障清理不能被 close 异常打断。摄像头故障通知使用独立的会话取消信号并限制发送时间，避免预览与自动循环相互取消通知。
- `FaceCaptureAgent/Models/` 中 YuNet ONNX、MIT 许可证和 SHA-256 说明属于源码，必须复制到构建与发布目录。复用 OpenCvSharp5.Windows CPU 运行库；模型输入按比例缩放并补黑至 320×320，置信度阈值 0.85。运行时不下载模型。
- Vue 只保留模式、状态、照片展示；浏览器 MediaPipe 模型、WASM、Worker 和预处理脚本已移除。不要恢复前端重复检测或自动触发。
- 稳定判断不是身份识别、活体检测或完整照片质量评估。合成帧与模型加载测试不能替代真实人脸、摄像头、目标整机性能及安装升级验收。

### 1.1.0 验证记录与后续验收

- 2026-09-09 完成 Agent 自动拍照迁移、SDK 事件接口及 Vue 接入，安装包默认版本为 1.1.0。实现与验证记录见 `docs/superpowers/plans/2026-09-08-agent-auto-mode.md`。
- 已通过 84 项 .NET Release 测试、11 项 SDK 测试、14 项 Vue 测试及前端生产构建。回归覆盖取消阻塞、设备清理异常、拍完后的预览故障、退出时发送阻塞，以及手动模式和检测失败后的摄像头关闭通知。
- 已使用新发布的 EXE 在独立本机端口验证摄像头索引 0、1：各完成一次稳定后自动抓拍、切回手动抓拍及关闭，图像尺寸为 1280×720，无检测错误；未保存照片。系统枚举到 Integrated Camera 和 Integrated IR Camera，尚未确认名称与索引的对应关系。
- 浏览器使用合成帧测试服务验证连接、预览、手动/自动切换、重新拍照、下载点击、关闭和断开；检查桌面及 390×844 移动视口，未发现相关控制台错误或横向溢出。
- 安装包已构建，发布模型 SHA-256 与源码一致；安装包和发布生成物不提交 Git。仍需在目标整机进行性能、光照、多人场景及隔离环境下的安装升级/卸载验收，不得将上述冒烟验证视为这些验收已完成。

## 编码风格与命名约定

C# 使用 4 空格缩进、文件作用域命名空间和已启用的可空引用类型。公开类型及成员使用 `PascalCase`，局部变量和参数使用 `camelCase`，异步方法以 `Async` 结尾。一个文件聚焦一个主要职责，并保持协议错误码稳定。JavaScript 使用 ES Module、2 空格缩进、分号和 `camelCase`；不要破坏 `FaceCaptureClient` 的既有公开接口。

## 测试指南

.NET 测试使用 xUnit。测试类命名为 `*Tests`，方法名采用 `被测行为_场景_预期结果`，例如 `ValidateQuality_RejectsOutOfRange`。功能修改应同步增加成功路径和错误路径测试。摄像头逻辑优先使用 `FakeCameraService`，真实 UVC 设备验证需在 PR 中单独记录。提交前必须运行上述两个测试命令。

## 桌面验证

托盘或日志变更需验证：单击图标显示日志、关闭窗口后仍在后台运行、重新打开显示历史与新日志、右键退出后连接关闭且摄像头释放。WebSocket 集成测试移除 `TrayService`，避免测试弹出托盘 UI；自动化测试通过不等于完成桌面交互或真实摄像头验证，应如实记录验证范围。

还需验证启动时窗口与任务栏图标可见，点击最小化按钮或已激活的任务栏图标后只保留托盘图标，点击托盘图标或“查看日志”后恢复窗口及任务栏图标。连续执行多轮，并检查关闭后重开。`Hosting/TrayLogWindowTests.cs` 在独立 STA 线程创建真实窗口，发送 `SC_MINIMIZE` 并检查原生可见性与最小化状态，运行时会短暂显示测试窗口，需要可用的 Windows 桌面。

## 托盘、日志与图标约定

- 所有 Windows Forms 控件在独立 STA 线程创建和访问；跨线程停止通过 UI 消息队列调度。
- `TrayLogWindow` 统一管理窗口隐藏和恢复；`TrayService` 的启动、托盘单击及“查看日志”入口均调用 `RestoreFromTray()`。窗口标题及托盘名称使用“刷脸认证”。
- 在 `WndProc` 中处理 `WM_SYSCOMMAND/SC_MINIMIZE`，直接隐藏窗口并阻止原生最小化继续执行。不要在 `Resize` 回调中切换 `ShowInTaskbar` 或重建句柄，以免窗口恢复后只有任务栏图标；隐藏窗口本身即可移除任务栏按钮。
- 关闭日志窗口只隐藏窗口；右键“退出”通过 `IHostApplicationLifetime.StopApplication()` 停止服务。活动连接必须关联 `ApplicationStopping` 取消信号，并执行摄像头清理。
- 日志只保留本次运行最近 1,000 条，包含时间、操作结果及失败错误码；不记录请求内容、照片、Base64 或其他个人数据，不写入磁盘。
- 统一使用方案 A「人脸取景」图标。修改 `Assets/face-capture.png` 后运行 `scripts/convert-icon.ps1`，验证透明背景和 16、20、24、32、48、64、128、256 像素帧。
- `face-capture.ico` 同时用于 EXE 的 `ApplicationIcon`、逻辑名为 `FaceCaptureAgent.AppIcon` 的嵌入资源及 Inno Setup 的 `SetupIconFile`。托盘不能依赖开发机绝对路径或外部未发布资源。

## Windows 安装包维护

`scripts/build-installer.ps1` 先执行发布与 Release 测试，再调用 Inno Setup 6。可通过 `-Version` 指定版本、`-IsccPath` 指定编译器。输出为 `installer-output/刷脸认证.exe`，包含 .NET、Windows Desktop、ASP.NET Core 运行时及摄像头依赖，目标电脑无需预装 .NET。

安装采用当前用户范围，安装后启动并注册 HKCU 登录自启动。保持 AppId 稳定，覆盖安装保留已有 `config.toml`；卸载停止进程时必须限定完整可执行路径，不能终止其他路径下的同名程序。

安装显示名称、开始菜单及桌面快捷方式使用“刷脸认证”。安装时自动在 `{userdesktop}` 创建启动快捷方式，卸载时移除。保留内部可执行文件名 `FaceCaptureAgent.exe`、默认目录 `%LocalAppData%\Programs\FaceCaptureAgent` 和自启动注册项名称，兼容旧版本覆盖安装。安装后启动不得使用 `runhidden`，以便显示窗口。修改安装包名称时同步更新构建脚本、安装测试及 README；安装测试的已有安装检查须同时识别新旧显示名称。

完整安装测试会安装、升级、修改自启动项并卸载，只在无现有安装、自启动注册和运行实例的隔离环境执行。不要为了通过测试自动卸载用户现有程序。发布清理必须先校验解析后的绝对目录仍是预期发布目录。

## 提交与拉取请求

沿用 Conventional Commits 风格，如 `feat: add Linux camera backend`、`fix: release camera after disconnect`、`docs: clarify demo startup`。每个提交只处理一个主题。PR 应说明变更目的、配置或协议影响、测试结果和关联问题；修改 `demo/` 时附桌面与移动端截图，涉及硬件时注明摄像头型号和操作系统。

## 安全与配置

服务必须仅监听 `127.0.0.1`。当前“不校验令牌和 Origin”是明确但有风险的设计，未经讨论不得扩大监听范围。不要提交 API 密钥、完整人脸 Base64、抓拍图片或包含个人数据的日志。

## 内置测试入口

Agent 的 .NET 构建会编译 `vue3-demo` 并把 dist 静态文件嵌入程序集，由 `/test/` 提供；不依赖运行目录或另外部署前端。日志窗口的“打开测试页”按钮在 ApplicationStarted 后启用，使用配置端口打开默认浏览器。维护时需验证 `/test/`、JS/CSS/图标资源、未知文件 404 和自定义端口；发布电脑不需要 Node.js，源码构建电脑需要 Node.js 22.12.0+。

### 2026-09-12 验证记录

- 已通过 87 项 .NET Release 测试、11 项 SDK 测试和 15 项 Vue 测试，并完成 Vue 生产构建、自包含发布与安装包编译。
- 已从源码目录之外启动发布版，在独立端口 18765 验证内置页面加载、默认 WebSocket 地址跟随端口、连接、枚举两台摄像头与断开；浏览器检查未发现页面错误日志。
- 已通过 Windows UI Automation 验证日志窗口“打开测试页”按钮可用并实际触发浏览器启动。该验证未覆盖浏览器启动失败分支；本次未重新进行真实拍照及隔离环境下的安装升级/卸载验收。
- 测试页仅发布 Vue 构建后的嵌入资源，未知路径返回 404，不提供源码目录或 config.toml。前端 lockfile 变更后须先运行 `npm --prefix vue3-demo ci` 再构建，避免复用过期依赖。
- 安装包仍输出到 `installer-output/刷脸认证.exe`；安装包、publish、dist 等生成物不得提交 Git。
