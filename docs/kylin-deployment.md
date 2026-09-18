# 麒麟 Linux 部署与验收

Linux 使用独立 `FaceCaptureAgent.Linux` 宿主，共享协议、会话和检测逻辑。发布目标为 `linux-arm64`（ARM64）或 `linux-x64`（AMD64），不能将 Windows 发布目录复制到 Linux 使用。当前提供目录式自包含发布；目标机无需另装 .NET，但仍须具备原生运行库和可用的 V4L2 摄像头。

## 构建发布目录

目标银河麒麟 V10 SP1 2403 ARM64 的 glibc 为 2.31。当前 `OpenCvSharp5.runtime.linux-arm64` 5.0.0.20260806 原生库要求 glibc 2.38，不能直接运行；ARM64 发布已设置拦截，必须先按[原生库构建说明](kylin-native-build.md)在兼容 Linux 环境生成原生库。该构建配方尚未在目标机执行。

源码构建环境需要 .NET 10 SDK、PowerShell、Node.js 22.12.0+、npm 和 Python 3（自定义原生库预检调用 `python`，Linux 发布检查默认调用 `python3`，对应命令需在 PATH 中可用）。仓库根目录运行：

```powershell
./scripts/publish-linux.ps1 -Runtime linux-arm64 -NativeLibraryDirectory 'D:\artifacts\kylin-arm64-native'
# Linux 上使用原生库目录的绝对路径，例如 /home/user/artifacts/kylin-arm64-native。
# x64 独立发布；也可以传入对应 x64 的 NativeLibraryDirectory：
./scripts/publish-linux.ps1 -Runtime linux-x64
```

输出 `publish/linux-arm64/` 或 `publish/linux-x64/`。脚本拒绝覆盖现有目录，应先将旧产物移到其他位置；失败后的不完整产物也不能交付。发布会检查启动程序、配置、OpenCV 原生 `.so` 和两个 ONNX 模型。应保留整个目录，包括模型许可、运行时库及 `installer/`，不要仅复制可执行文件。

发布会检查 ELF 架构和所需 GLIBC 版本（上限 2.31），但不证明目标机具备全部动态依赖，也不证明模型、GTK 或摄像头可用。每种目标架构和麒麟版本均须单独验收；当前没有通过实机验收的麒麟 ARM64 安装产物。

## 目标机准备

在实际图形桌面登录的普通用户会话中操作，不使用 `sudo` 安装，不设置 systemd linger。需要 Bash、GNU coreutils、grep、sed、systemd 用户管理器，以及快捷入口使用的 `curl`、`xdg-open`。图形窗口需要 GTK 3。登录自启使用桌面的 XDG autostart 支持，不依赖桌面是否启动 `graphical-session.target`；仍须在目标桌面验证实际登录行为。

确认 `uname -m` 与发布架构一致。将发布目录传到目标机后执行：

```bash
cd /path/to/extracted/linux-arm64
ldd ./FaceCaptureAgent.Linux
ldd ./libOpenCvSharpExtern.so
bash ./installer/install.sh
```

`ldd` 不得出现 `not found`；glibc、libstdc++、视频编解码等库的版本必须满足所用原生包要求。具体发行版的软件包名和兼容性须在目标机确认，不能用修改库文件软链接强行绕过 ABI 问题。安装只针对当前用户：

| 内容 | 路径 |
| --- | --- |
| 程序、模型和运行时 | `~/.local/share/FaceCaptureAgent/` |
| 用户配置 | `~/.config/FaceCaptureAgent/config.toml` |
| 用户服务 | `~/.config/systemd/user/face-capture-agent.service` |
| 应用菜单快捷入口 | `~/.local/share/applications/face-capture-agent.desktop` |
| 桌面登录自启入口 | `~/.config/autostart/face-capture-agent.desktop` |

安装器拒绝程序目录和管理文件中的符号链接路径，拒绝接管没有安装标记的同名目录或服务文件。HOME 路径不支持双引号、百分号、反斜杠或换行。首次安装复制默认配置，升级保留现有配置。不要将自己的其他文件存放在程序目录中；升级会替换整个受管理目录。升级前应备份旧发布目录，安装失败可重新安装旧版本；此脚本不提供原子版本回滚。

桌面登录时自启入口导入当前会话已有的 DISPLAY、WAYLAND_DISPLAY、XAUTHORITY 和 DBUS_SESSION_BUS_ADDRESS 到用户服务管理器，再启动服务。应用菜单“刷脸认证”通过同一个用户服务启动 Agent，然后打开配置端口的 `/test/`，重复点击不另起进程。Linux 宿主提供 GTK 桌面窗口，具体托盘支持取决于目标桌面。标准输出和标准错误送到 `null`，不持久化应用日志；systemd 自身仍可能记录服务生命周期等系统元数据。

## 配置与日常操作

排查不依赖桌面的服务问题时，可从发布目录手动运行 `./FaceCaptureAgent.Linux --headless --config /absolute/path/config.toml`。此模式不创建 GTK 窗口；请先停止同端口的已安装服务。正常桌面模式的托盘支持与最小化行为仍需在目标桌面验证。

编辑 `~/.config/FaceCaptureAgent/config.toml` 后重启：

```bash
systemctl --user restart face-capture-agent.service
systemctl --user status face-capture-agent.service
# 打开测试页（等待服务就绪，读取 listen_port）
bash ~/.local/share/FaceCaptureAgent/installer/launch.sh
# 停止本次运行（下次登录仍自启）
systemctl --user stop face-capture-agent.service
```

如需禁用登录自启，在桌面“启动应用程序”中关闭此项，或将 autostart 文件的 `X-GNOME-Autostart-enabled` 改为 `false` 并添加 `Hidden=true`；恢复时改回 `true` 并移除 `Hidden=true`。重新安装会恢复默认自启入口。

监听地址必须为 `127.0.0.1`，默认端口 `17653`。摄像头访问由当前桌面用户的 `/dev/video*` 权限决定，不要以 root 运行 Agent 或放宽所有设备权限。确保没有其他程序独占设备。外部 HTTPS 业务站点连接本机 WebSocket 的浏览器限制仍需独立验证。

## 升级与卸载

从新的发布目录运行 `bash installer/install.sh` 即可升级；它先检查发布文件，再停止本服务并替换程序。用户配置保持不变。卸载：

```bash
bash ~/.local/share/FaceCaptureAgent/installer/uninstall.sh
```

卸载只停止 `face-capture-agent.service`，移除有安装标记的本用户程序目录、服务、应用及自启入口，不按进程名称批量终止程序。默认保留用户配置。若确需清理配置，请在确认无须保留后手工处理这个明确的目录。

## 目标机验收清单与已知验证范围

- 在干净用户下首次安装、重复升级、保留修改过的配置，退出桌面再登录检查自动启动。
- 多次点击快捷入口后确认只有一个服务主进程；检查本机端口仅绑定回环地址。
- `/test/` 和静态资源可加载，修改端口后入口跟随；浏览器能连接、枚举 V4L2 设备、预览、手动及自动拍照、取消并释放摄像头。
- 验证无摄像头、设备占用、拔插、端口占用和异常退出后的行为；检查 ARM64 与 x64 原生库加载和实际性能。
- 卸载后确认服务停止、程序目录和应用入口删除、配置保留；其他同名进程和无关文件不受影响。

本轮在 Windows 完成共享核心测试、Windows 回归与发布检查，并通过 Python ABI 检查器的 4 项测试、Bash 语法检查和 Git Bash 下使用临时 HOME、模拟 systemctl 的安装脚本测试。Git Bash 不提供该测试所需的真实符号链接行为，相应用例明确跳过。没有在真实麒麟或 Linux systemd 用户会话执行安装、升级、卸载、GTK 或摄像头验收；原生库构建配方也尚待目标环境执行。上述检查不能替代实机测试。
