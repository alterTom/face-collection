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

前两项恢复依赖并验证 .NET 服务；Node 命令验证浏览器 SDK；`dotnet run` 启动本机服务；演示脚本在 `http://127.0.0.1:18080/demo/` 提供页面；发布脚本生成 Windows x64 自包含产物。

## 编码风格与命名约定

C# 使用 4 空格缩进、文件作用域命名空间和已启用的可空引用类型。公开类型及成员使用 `PascalCase`，局部变量和参数使用 `camelCase`，异步方法以 `Async` 结尾。一个文件聚焦一个主要职责，并保持协议错误码稳定。JavaScript 使用 ES Module、2 空格缩进、分号和 `camelCase`；不要破坏 `FaceCaptureClient` 的既有公开接口。

## 测试指南

.NET 测试使用 xUnit。测试类命名为 `*Tests`，方法名采用 `被测行为_场景_预期结果`，例如 `ValidateQuality_RejectsOutOfRange`。功能修改应同步增加成功路径和错误路径测试。摄像头逻辑优先使用 `FakeCameraService`，真实 UVC 设备验证需在 PR 中单独记录。提交前必须运行上述两个测试命令。

## 桌面验证

托盘或日志变更需验证：单击图标显示日志、关闭窗口后仍在后台运行、重新打开显示历史与新日志、右键退出后连接关闭且摄像头释放。WebSocket 集成测试移除 `TrayService`，避免测试弹出托盘 UI；自动化测试通过不等于完成桌面交互或真实摄像头验证，应如实记录验证范围。

## 托盘、日志与图标约定

- 所有 Windows Forms 控件在独立 STA 线程创建和访问；跨线程停止通过 UI 消息队列调度。
- 关闭日志窗口只隐藏窗口；右键“退出”通过 `IHostApplicationLifetime.StopApplication()` 停止服务。活动连接必须关联 `ApplicationStopping` 取消信号，并执行摄像头清理。
- 日志只保留本次运行最近 1,000 条，包含时间、操作结果及失败错误码；不记录请求内容、照片、Base64 或其他个人数据，不写入磁盘。
- 统一使用方案 A「人脸取景」图标。修改 `Assets/face-capture.png` 后运行 `scripts/convert-icon.ps1`，验证透明背景和 16、20、24、32、48、64、128、256 像素帧。
- `face-capture.ico` 同时用于 EXE 的 `ApplicationIcon`、逻辑名为 `FaceCaptureAgent.AppIcon` 的嵌入资源及 Inno Setup 的 `SetupIconFile`。托盘不能依赖开发机绝对路径或外部未发布资源。

## Windows 安装包维护

`scripts/build-installer.ps1` 先执行发布与 Release 测试，再调用 Inno Setup 6。可通过 `-Version` 指定版本、`-IsccPath` 指定编译器。输出为 `installer-output/FaceCaptureAgent-Setup-x64.exe`，包含 .NET、Windows Desktop、ASP.NET Core 运行时及摄像头依赖，目标电脑无需预装 .NET。

安装采用当前用户范围，安装后启动并注册 HKCU 登录自启动。保持 AppId 稳定，覆盖安装保留已有 `config.toml`；卸载停止进程时必须限定完整可执行路径，不能终止其他路径下的同名程序。

完整安装测试会安装、升级、修改自启动项并卸载，只在无现有安装、自启动注册和运行实例的隔离环境执行。不要为了通过测试自动卸载用户现有程序。发布清理必须先校验解析后的绝对目录仍是预期发布目录。

## 提交与拉取请求

沿用 Conventional Commits 风格，如 `feat: add Linux camera backend`、`fix: release camera after disconnect`、`docs: clarify demo startup`。每个提交只处理一个主题。PR 应说明变更目的、配置或协议影响、测试结果和关联问题；修改 `demo/` 时附桌面与移动端截图，涉及硬件时注明摄像头型号和操作系统。

## 安全与配置

服务必须仅监听 `127.0.0.1`。当前“不校验令牌和 Origin”是明确但有风险的设计，未经讨论不得扩大监听范围。不要提交 API 密钥、完整人脸 Base64、抓拍图片或包含个人数据的日志。
