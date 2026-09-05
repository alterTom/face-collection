<script setup>
import { onBeforeUnmount, onMounted, ref } from 'vue';
import { createCaptureController } from './capture-controller.js';
import PreviewPanel from './components/PreviewPanel.vue';
import CaptureResult from './components/CaptureResult.vue';
import logo from '../../FaceCaptureAgent/Assets/face-capture.png';

const capture = createCaptureController();
const { state, photo, photoUrl } = capture;
const preview = ref(null);
const release = () => capture.disconnect(false);
onMounted(() => window.addEventListener('pagehide', release));
onBeforeUnmount(() => {
  window.removeEventListener('pagehide', release);
  release();
});
</script>

<template>
  <header class="site-header">
    <div class="brand"><img :src="logo" alt="" /> <span>FaceCapture</span></div>
    <span class="demo-label">Vue 3 Demo</span>
  </header>
  <main>
    <div class="page-heading">
      <div><h1>人脸照片采集</h1><p>连接本机采集服务，预览并抓拍照片。</p></div>
      <div class="connection-status" role="status" aria-live="polite">
        <span>连接状态：</span>
        <strong :class="{ online: state.connected }">{{ state.connected ? '已连接' : state.busy === '连接服务' ? '连接中…' : '未连接' }}</strong>
      </div>
    </div>
    <div v-if="state.error" class="error-message" role="alert">{{ state.error }}</div>
    <div class="workspace">
      <PreviewPanel ref="preview" :camera-open="state.cameraOpen" :busy="state.busy" :resolution="state.resolution" @capture="capture.capture" />
      <aside class="panel control-panel" aria-label="采集控制及结果">
        <section aria-labelledby="control-heading">
          <h2 id="control-heading">采集控制</h2>
          <label for="service-url">服务地址</label>
          <input id="service-url" v-model="state.url" type="url" spellcheck="false" :disabled="state.connected || !!state.busy" />
          <div class="button-row connection-buttons">
            <button class="primary" :disabled="state.connected || !!state.busy" @click="capture.connect">{{ state.busy === '连接服务' ? '正在连接…' : '连接服务' }}</button>
            <button class="secondary" :disabled="!state.connected && !state.busy" @click="capture.disconnect()">断开连接</button>
          </div>
          <label for="camera-select">摄像头</label>
          <div class="device-row">
            <select id="camera-select" v-model="state.deviceId" :disabled="!state.connected || state.cameraOpen || !!state.busy">
              <option v-if="!state.devices.length" value="">{{ state.connected ? '未发现摄像头' : '请先连接服务' }}</option>
              <option v-for="device in state.devices" :key="device.id" :value="device.id">{{ device.name }}</option>
            </select>
            <button class="secondary" :disabled="!state.connected || state.cameraOpen || !!state.busy" @click="capture.refreshDevices">{{ state.busy === '刷新设备' ? '刷新中…' : '刷新设备' }}</button>
          </div>
          <div class="button-row">
            <button class="primary" :disabled="!state.connected || !state.deviceId || state.cameraOpen || !!state.busy" @click="capture.open(preview.image)">{{ state.busy === '打开摄像头' ? '正在打开…' : '打开摄像头' }}</button>
            <button class="secondary" :disabled="!state.cameraOpen || !!state.busy" @click="capture.close">{{ state.busy === '关闭摄像头' ? '正在关闭…' : '关闭摄像头' }}</button>
          </div>
        </section>
        <CaptureResult :photo="photo" :photo-url="photoUrl" />
      </aside>
    </div>
    <section class="panel log-panel" aria-labelledby="log-heading">
      <div class="panel-heading"><h2 id="log-heading">操作日志</h2><button class="text-button" :disabled="!state.logs.length" @click="capture.clearLogs">清空</button></div>
      <div class="log-list" role="log" aria-live="polite" aria-relevant="additions">
        <p v-if="!state.logs.length" class="log-empty">等待连接本机服务</p>
        <div v-for="entry in state.logs" :key="entry.id" class="log-entry" :class="entry.tone"><time>{{ entry.time }}</time><span>{{ entry.message }}</span></div>
      </div>
    </section>
    <footer>照片仅保留在当前页面，点击下载后保存到本机。</footer>
  </main>
</template>
