# face-capture-vue

Vue 3 人脸照片采集弹窗组件。打开即连接本机 Agent、实时预览并自动抓拍；成功后关闭弹窗，将照片返回调用方。只包含取景区、状态提示、连接倒计时、「重新拍照」「退出认证」，不显示设备配置或日志。

## 安装与接入

当前为本地交付版本，尚未发布 npm。将生成的安装包复制到业务项目后安装：

```sh
npm install ./face-capture-vue-0.1.0.tgz
```

业务项目需要 Vue **3.5 或更高的 3.x 版本**。Vue 为 peer dependency，组件不会重复打包 Vue。SDK 已包含在构建产物中，安装方不需要本仓库、Vite 或 Agent 源码。

```vue
<script setup lang="ts">
import { ref } from 'vue';
import { FaceCaptureDialog, type CaptureResult } from 'face-capture-vue';
import 'face-capture-vue/style.css';

const visible = ref(false);
const photo = ref<Blob | null>(null);
const message = ref('');

function handleResult(result: CaptureResult) {
  switch (result.status) {
    case 'success':
      photo.value = result.photo.blob;
      message.value = '照片采集成功';
      // 也可使用 result.photo.base64（不带 Data URI 前缀）。
      // 如需上传，由业务方将 Blob 放入 FormData 并调用自己的接口。
      break;
    case 'cancelled':
      message.value = '用户已退出采集';
      break;
    case 'connection_failed':
    case 'error':
      message.value = result.message;
      break;
  }
}
</script>

<template>
  <button @click="visible = true">开始采集</button>
  <p>{{ message }}</p>
  <FaceCaptureDialog
    v-model="visible"
    service-url="ws://127.0.0.1:17653/face"
    :connection-timeout-ms="60000"
    :stable-duration-ms="1500"
    @result="handleResult"
  />
</template>
```

JavaScript 项目去掉 `lang="ts"`、`type CaptureResult`、函数参数类型和 `ref` 泛型即可。

## 属性

| 属性 | 类型 | 默认值 | 含义 |
| --- | --- | --- | --- |
| `modelValue` / `v-model` | boolean | `false` | 打开或关闭弹窗；调用方主动设为 false 会取消采集 |
| `serviceUrl` | string | `ws://127.0.0.1:17653/face` | 本机服务地址，仅允许 localhost / 127.0.0.1 的 ws/wss |
| `deviceId` | string | 不指定 | 默认使用 Agent 枚举的第一台设备；需要指定设备时传其 ID |
| `stableDurationMs` | number | `1500` | 人脸稳定时长，500–10000 的整数毫秒 |
| `connectionTimeoutMs` | number | `60000` | 连接等待上限，1–2147483647 的整数毫秒 |
| `title` | string | `人脸采集` | 弹窗标题 |

配置在每次打开弹窗时读取，弹窗打开期间修改配置不会改变当前会话。重新打开后使用新配置。页面应同时只打开一个采集弹窗，以避免竞争同一摄像头。

## 结果事件

每次打开最多触发一次 `result`。触发前组件关闭弹窗、发出 `update:modelValue(false)`，断开连接并清理预览；Agent 随会话结束释放摄像头。回调不表示已完成身份比对或认证。

```ts
type CaptureResult =
  | { status: 'success'; photo: CapturePhoto }
  | { status: 'cancelled' }
  | { status: 'connection_failed'; code: 'AGENT_CONNECTION_TIMEOUT'; message: string }
  | { status: 'error'; code: 'INVALID_OPTIONS'; message: string };

interface CapturePhoto {
  base64: string;
  blob: Blob;
  mimeType: 'image/jpeg';
  width: number;
  height: number;
  size: number; // 实际 JPEG 字节数
  capturedAt?: string;
}
```

- **连接倒计时**：弹窗打开即开始计时；每次连接尝试最多 2 秒，失败后约 1 秒重试。重试共享同一截止时间，不会刷新 60 秒。超时关闭并返回 `connection_failed`。
- **连接成功**：立即停止连接倒计时，随后枚举设备、打开摄像头、预览并等待自动抓拍。拍照没有 60 秒限制。
- **重新拍照**：检测期间重置当前拍照轮；设备或检测出错时清理旧连接并重新连接，此时开始新一轮连接倒计时。连接中或打开设备期间按钮禁用。
- **退出**：退出按钮、右上角关闭、Escape、调用方关闭及组件卸载均取消会话，返回 `cancelled`；页面离开时也清理连接。
- **错误恢复**：摄像头缺失、占用、检测错误或已连接后的服务断开会保留弹窗，显示筛选后的中文提示，可重新拍照或退出。无效参数则关闭并返回 `error`。
- **照片**：只在内存中交付，不自动下载、上传或打印照片数据。圆形裁切及镜像仅用于预览，返回的是 Agent 原始 JPEG。调用方使用 `URL.createObjectURL` 展示 Blob 时负责 `URL.revokeObjectURL`。

## 环境要求

- 终端必须安装并运行支持自动抓拍的本机 `FaceCaptureAgent`（本仓库 1.1.0 或后续兼容版）；前端不使用 `getUserMedia`，不加载检测模型。
- 使用支持原生 `dialog.showModal()`、WebSocket、Blob 的现代浏览器；弹窗使用原生模态焦点管理与 Vue Teleport。组件样式有独立作用域，不向宿主注入 body 或全局按钮样式。
- 仅客户端挂载后连接服务；SSR 场景需由宿主处理 Teleport，或在客户端容器中挂载该组件。
- 目标为 Windows 本机 Agent。移动视口验证表示布局适配，并不意味着手机可访问电脑的 localhost。
- HTTPS 页面连接本机服务的混合内容、本地网络访问和证书限制仍需在目标部署环境验证。安装 npm 包不会自动解决这些限制。

## 本仓库开发与验证

源码构建需要保留同级 `web-sdk/`；测试使用同级 `vue3-demo/tests/fixtures/frame.jpg` 合成图像。现有 `vue3-demo`、SDK 和 Agent 源码不作修改。

在本目录执行（Node.js 22.12+）：

```sh
npm ci
npm test
npm run typecheck
npm run dev
```

示例地址为 `http://127.0.0.1:5175`。测试 Agent 另开终端运行 `npm run test:agent`，示例服务地址设置为 `ws://127.0.0.1:17658/face`，约 12 秒后返回合成帧；`/idle` 仅预览不抓拍，`/busy` 返回摄像头占用。该测试服务不进行人脸检测，不得接入生产。

`npm test` 先构建，再运行会话测试与打包产物的 Vue DOM/WebSocket 集成测试。类型检查使用固定 TypeScript 5.9.3，与当前 Vue 类型检查工具兼容。真实摄像头、目标设备性能及正式 HTTPS 环境需额外验收。

## 打包与后续发布

```sh
npm pack
```

生成 `face-capture-vue-0.1.0.tgz`，只包含编译后的 ESM、CSS、类型声明、README 和包元数据，不含测试服务、源码依赖路径或照片。可以将该文件交付其他项目安装。

`face-capture-vue` 是当前本地包名，尚未核实 npm 名称可用性。正式发布前确定包名/组织 scope、使用许可及公开或私有方式，再使用具有对应权限的 npm 账号发布。当前 `UNLICENSED` 未授予开源许可；本任务不会执行 npm publish。
