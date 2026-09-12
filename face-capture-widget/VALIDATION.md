# 验证记录（2026-09-12）

## 取景组件拆分后的补充验证

- `FaceCapture` 仅包含取景框和连接旋转动画；`FaceCaptureDialog` 组合前者保持旧接口。新增自定义面板示例，使用 v-show 保留组件、active 控制资源。
- 20 项会话/组件测试通过，含 active=false 释放资源、终态后不重复拍照、重新激活、公开 retake，以及启动途中立即取消的两项回归测试。类型检查与构建通过。
- SDK 11 项、Agent Release 87 项回归测试通过；NuGet 漏洞数据源仍产生 NU1900 警告。
- 浏览器验证自定义面板实时预览、重新采集、关闭后重新激活、成功返回照片；移动视口页面 clientWidth 与 scrollWidth 均为 375，未出现横向溢出，未发现页面错误/警告。
- 修复宿主示例全局图片 max-height 对预览的影响，确认预览显示尺寸为 330×330。
- 代码复核发现并修复了会话创建前取消的竞态，回归测试先复现失败后通过。更新的 tarball 导出两个组件。
- 独立消费项目重新安装更新的 tarball，验证 `FaceCapture` 和 `FaceCaptureHandle` 的导入，类型检查及 Vite 生产构建通过。包仍为本地交付版本，未发布 npm。

以下为首次交付记录，组件数量和包大小以最新构建为准。

## 自动化与安装包

- `npm test`：16 项通过。包含独立会话状态测试，以及构建产物在 Vue DOM 环境中使用真实 WebSocket 连接合成测试服务的集成测试。
- `npm run typecheck`：Vue TypeScript 接入示例通过。
- `npm pack`：生成 `face-capture-vue-0.1.0.tgz`，5 个文件，约 12.4 kB；Vue 外置，SDK 已打包。包内不含测试服务和示例。
- `.qa/consumer/` 从 tarball 独立安装，`npm ls --depth=0`、类型检查和 Vite 生产构建通过。
- 既有 SDK：11 项测试通过。
- `dotnet test FaceCaptureAgent.Tests/FaceCaptureAgent.Tests.csproj --no-restore -c Release`：87 项通过。NuGet 漏洞数据源不可达产生 NU1900 警告，测试没有失败。
- 代码复核未发现需要修复的问题。
- Git 检查确认现有 `vue3-demo`、SDK 和 Agent 源码没有修改。生成的 dist、node_modules、tarball 和 `.qa/` 不纳入 Git。

## 浏览器

使用 Codex 内置浏览器，示例 `http://127.0.0.1:5175`，测试服务 `ws://127.0.0.1:17658`，只显示无个人信息的合成帧。

| 检查 | 结果 |
| --- | --- |
| 页面标题、内容加载、无 Vite 错误遮罩 | 通过 |
| 1280×720 桌面弹窗 | 通过，取景区和按钮可见 |
| 390×844 移动视口 | 通过，页面宽度与 scrollWidth 均为 390，弹窗在视口内 |
| 自动连接、实时预览、连接倒计时停止 | 通过 |
| 重新拍照 | 通过，重置检测并显示自动拍照状态 |
| 退出认证 | 通过，弹窗关闭，调用页显示用户已取消 |
| 自动抓拍 | 通过，弹窗关闭，调用页显示 640×480 合成照片 |
| 完整 60 秒连接超时 | 通过，倒计时结束关闭弹窗，调用页显示「60秒内未能连接本机采集服务」 |
| 相关页面控制台错误/警告 | 上述成功与取消流程未发现 |

截图保存在本地 `.qa/desktop-preview.jpg`、`.qa/mobile-preview.jpg` 和 `.qa/connection-countdown.jpg`，不进入 npm 包。

本记录不代表真实摄像头、人脸检测质量、目标机器性能、HTTPS 部署或安装升级验收已经完成。移动视口仅验证响应式布局。
