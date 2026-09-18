# Windows 适配测试包验证记录

本轮按用户要求打包当前共享核心改造后的 Windows x64 版本，应用版本维持 1.1.3，安装包名称包含构建日期以便区分。

修复了 `SkipTestPageBuild=true` 时跳过整个资源嵌入目标的问题：现在仅跳过 npm 构建，仍嵌入预先构建的 dist；缺少 index.html 则明确构建失败。该路径用于先独立构建前端再执行 .NET 构建。

已验证：

- `npm --prefix vue3-demo run build` 成功。
- `dotnet test FaceCaptureAgent.Tests/FaceCaptureAgent.Tests.csproj --no-restore -c Release -p:SkipTestPageBuild=true`：154 项通过，包括内置 HTML 和资源测试。还原缓存中有 NuGet 漏洞数据 SSL 查询警告；后续发布还原成功。
- `node --test web-sdk/face-capture.test.mjs`：15 项通过。
- Windows x64 自包含单文件发布成功；源模型及许可证与发布目录的 SHA-256 一致。
- 从源码目录以外启动发布 EXE，使用独立端口 18766：`/test/` 和三项资源均返回 200，WebSocket `system.info` 返回 `platform=win-x64`、`os=windows`。检查结束后仅停止本轮启动的进程。
- Inno Setup 编译成功：`installer-output/刷脸认证-Windows-x64-20260919.exe`，95,207,126 字节。未执行安装包。

上述发布检查未打开摄像头、未拍照、未安装或卸载应用。真实采集、桌面交互以及安装升级卸载由目标 Windows 机器继续验收。旧发布目录保留在 publish 下的带时间戳备份目录；安装包与发布文件均为忽略的生成物。
