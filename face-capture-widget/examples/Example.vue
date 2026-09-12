<script setup>
import { onBeforeUnmount, ref } from 'vue';
import { FaceCaptureDialog } from '../src/index.js';
import CustomCapture from './CustomCapture.vue';
const visible = ref(false), result = ref('尚未采集'), photoUrl = ref('');
const serviceUrl = ref('ws://127.0.0.1:17653/face');
const timeout = ref(60_000);
function clearPhoto() { if (photoUrl.value) URL.revokeObjectURL(photoUrl.value); photoUrl.value = ''; }
function receive(value) {
  clearPhoto();
  result.value = value.status === 'success' ? `采集成功：${value.photo.width} × ${value.photo.height}`
    : value.status === 'cancelled' ? '用户已取消' : value.message;
  if (value.status === 'success') photoUrl.value = URL.createObjectURL(value.photo.blob);
}
onBeforeUnmount(clearPhoto);
</script>
<template>
  <main class="example">
    <p class="eyebrow">FACE CAPTURE · VUE 3</p>
    <h1>人脸采集组件</h1><p>由业务系统打开弹窗，采集完成后接收照片。</p>
    <label>本机服务地址<input v-model="serviceUrl" /></label>
    <label>连接超时（毫秒）<input v-model.number="timeout" type="number" min="1" /></label>
    <button @click="visible = true">开始人脸采集</button>
    <p role="status">{{ result }}</p><img v-if="photoUrl" :src="photoUrl" alt="调用方收到的照片" />
    <p class="note">本页面中的地址和超时配置用于联调，不会显示在组件弹窗中。照片仅保存在内存。</p>
    <FaceCaptureDialog v-model="visible" :service-url="serviceUrl" :connection-timeout-ms="timeout" @result="receive" />
    <CustomCapture :service-url="serviceUrl" :connection-timeout-ms="timeout" />
  </main>
</template>
<style>
body { margin: 0; background: #f5f7fc; font-family: "Segoe UI", "Microsoft YaHei", sans-serif; color: #26314c; }
.example { max-width: 650px; margin: 8vh auto; padding: 28px; }
.example .eyebrow { color: #465dce; font-size: 12px; letter-spacing: 2px; }
.example h1 { font-size: 32px; }
.example label { display: block; margin: 20px 0; font-size: 14px; }
.example input { display: block; box-sizing: border-box; width: 100%; margin-top: 8px; padding: 12px; border: 1px solid #d5dcec; border-radius: 8px; font: inherit; }
.example button { padding: 12px 24px; border: 0; border-radius: 8px; background: #465dce; color: white; font: inherit; cursor: pointer; }
.example img { display: block; max-width: 100%; max-height: 260px; }
.example .note { font-size: 13px; line-height: 1.8; color: #737d92; }
</style>
