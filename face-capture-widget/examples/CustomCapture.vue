<script setup>
import { onBeforeUnmount, ref } from 'vue';
import { FaceCapture } from '../src/index.js';
defineProps({ serviceUrl: String, connectionTimeoutMs: Number });
const active = ref(false), capture = ref(null), state = ref(null), result = ref(''), photoUrl = ref('');
function clearPhoto() { if (photoUrl.value) URL.revokeObjectURL(photoUrl.value); photoUrl.value = ''; }
function receive(value) {
  active.value = false;
  clearPhoto();
  result.value = value.status === 'success' ? '自定义页面已收到照片'
    : value.status === 'cancelled' ? '自定义采集已退出' : value.message;
  if (value.status === 'success') photoUrl.value = URL.createObjectURL(value.photo.blob);
}
onBeforeUnmount(clearPhoto);
</script>
<template>
  <section class="custom-example">
    <h2>自定义页面接入</h2>
    <p>下方标题、提示和按钮由调用页面提供，采集组件仅显示取景框。</p>
    <button :disabled="active" @click="active = true">打开自定义采集面板</button>
    <section v-show="active" class="custom-panel" aria-label="业务自定义采集面板">
      <h3>请完成照片采集</h3>
      <FaceCapture ref="capture" :active="active" :service-url="serviceUrl"
        :connection-timeout-ms="connectionTimeoutMs" @state-change="state = $event" @result="receive" />
      <p role="status">{{ state?.message }}</p>
      <p v-if="state?.remainingSeconds != null">连接剩余 {{ state.remainingSeconds }} 秒</p>
      <div class="custom-actions">
        <button :disabled="!state?.canRetake" @click="capture?.retake()">重新采集</button>
        <button @click="active = false">关闭面板</button>
      </div>
    </section>
    <p role="status">{{ result }}</p>
    <img v-if="photoUrl" :src="photoUrl" alt="自定义页面收到的照片" />
  </section>
</template>
<style scoped>
.custom-example { margin-top: 40px; padding-top: 24px; border-top: 1px solid #d5dcec; }
.custom-panel { margin-top: 20px; padding: 24px; background: white; border: 1px solid #d5dcec; border-radius: 12px; text-align: center; }
.custom-actions { display: flex; gap: 12px; justify-content: center; flex-wrap: wrap; }
button:disabled { opacity: .5; cursor: not-allowed; }
</style>
