# 麒麟 glibc 2.31 原生 OpenCV 构建

此文档提供**目标环境中待执行的构建配方**。没有在当前 Windows 开发环境执行 Linux 编译，也没有完成麒麟摄像头或人脸模型验收。

已检查的 `OpenCvSharp5.runtime.linux-arm64 5.0.0.20260806` 原生库要求 `GLIBC_2.38`，不能直接用于 glibc 2.31 目标机。不能通过改文件名、替换系统 libc 或忽略加载错误修复此兼容性问题。需要在同架构且 glibc 不高于 2.31 的环境重建原生库，同时保留当前托管包 ABI。

## 构建环境

- 在真实 ARM64 麒麟开发机，或 ARM64 Linux glibc ≤ 2.31 构建环境中运行。脚本也支持本机 x64，输出对应 `linux-x64`。不做交叉编译；ARM64 构建环境只能产生 ARM64 产物。
- 预先由环境维护者安装 Git、CMake ≥ 3.15、Ninja、Python 3、binutils（readelf）、GNU coreutils、C/C++ 编译器与 Linux/V4L2 开发头文件。
- C++ 编译器必须完整支持 C++20，并提供可静态链接的 libstdc++、libgcc。老系统默认 GCC 可能不满足要求；使用适配该老系统的工具链，不能从新发行版直接拷贝二进制编译器。
- 需要访问官方 GitHub 源码。构建可能耗时较长且占用数 GB；默认只使用 2 个并行任务，按机器内存调整 `JOBS`。
- 脚本不运行 sudo、不安装系统文件、不删除已有目录。输出路径必须为不存在的绝对路径，其父目录须存在，且不能含符号链接或 `..`。

```bash
cd /path/to/windows-poc
JOBS=2 CC=gcc CXX=g++ bash scripts/build-kylin-native.sh "$HOME/face-native-kylin-build-01"
```

失败时保留构建目录、日志及缓存，供排查；重新执行请使用新的目录。不要将脚本退出失败后的零散 `.so` 当作交付产物。

## 固定版本与构建范围

脚本固定 OpenCvSharp 提交 [`a390f59aa0b2448e08b1eb2604507f5f5822c7da`](https://github.com/shimat/opencvsharp/tree/a390f59aa0b2448e08b1eb2604507f5f5822c7da)，对应托管包 `5.0.0.20260806`。OpenCV 使用该提交 gitlink 固定的子模块提交（OpenCV 5.0.0），不跟踪浮动分支或最新 tag。产物清单记录两者实际 SHA。

OpenCV 静态、PIC 编译；JPEG、PNG、zlib、protobuf 使用源码内的第三方版本。保留 core、imgproc、imgcodecs、videoio、objdetect、dnn，以及 OpenCV 自动要求的传递模块。保留 YuNet 的 FaceDetectorYN、FaceMesh 所需 DNN、JPEG 编解码和 V4L2；关闭 FFmpeg、GStreamer、GTK、Qt、OpenCL、CUDA、Tesseract、ONNX Runtime 预编译下载等不需要的依赖。

OpenCvSharpExtern 使用上游提供的 `NO_*` 开关裁剪绑定，保留 `NO_DNN=OFF`、`NO_OBJDETECT=OFF`、`NO_VIDEOIO=OFF`。链接静态 C++/GCC 运行库，避免要求目标机安装较新的 `GLIBCXX` 版本。`CMAKE_ASM_COMPILER` 设为空以避开此 OpenCV 5 构建路径的 MLAS 汇编符号问题（`MlasHGemmSupported`）；最终链接启用 `-z defs` 并通过原生立即加载检查，不能用允许未解析符号绕过失败。

此配方固定源码与构建选项，便于重复构建与审计；不承诺不同编译器、系统包或构建路径产生逐字节相同的二进制。

## 脚本检查与输出

脚本检查构建主机 glibc ≤ 2.31、本机架构、C++20 和静态运行库链接能力。编译后通过 readelf 检查 ELF 架构、GLIBC 符号版本、共享库依赖白名单；拒绝动态 libstdc++、OpenCV、媒体框架及其它意外依赖。Python 使用 `RTLD_NOW` 加载实际 `libOpenCvSharpExtern.so`，调用 `core_Mat_sizeof` 并确认人脸检测、ONNX、视频与 JPEG 绑定导出。另一个 C++ 冒烟程序核对同一 OpenCV 安装的构建信息包含 V4L2，并编码、解码合成 JPEG。

检查通过后才创建 `artifact/`，包含：

- `libOpenCvSharpExtern.so`。
- `build-info.txt`、原生检查和 OpenCV 构建信息。
- 两份 CMakeCache、构建脚本、SHA256SUMS。
- OpenCvSharp、OpenCV 和第三方许可证/通知副本，供发布前许可审查；这些副本不替代最终分发许可审查。

将整个 `artifact/` 保留为构建证据。发布时通过 Linux 发布脚本的 `-NativeLibraryDirectory` 参数传入该目录，使用目标架构相同的产物；发布工具仍须独立验证 ELF 和 glibc 要求。发布目录中保留原生库许可证和构建记录。

这些检查证明基础加载、必要导出、编译配置和 JPEG 通路，不证明模型推理与摄像头工作。目标麒麟机仍需验证：启动、实际 `/dev/videoN` 枚举、V4L2 取帧、预览、手动抓拍、YuNet/FaceMesh 模型加载和自动抓拍、关闭与再次打开，以及目标光照和性能。摄像头权限、模型算子兼容性和性能问题必须分别处理，不得以构建通过替代验收。
