<script setup>
import { ref } from 'vue';
defineProps({ cameraOpen: Boolean, busy: String, resolution: String,
  captureMode: { type: String, default: 'manual' }, autoStatus: String, autoComplete: Boolean });
defineEmits(['capture', 'retake']);
const image = ref(null);
defineExpose({ image });
</script>

<template>
  <section class="panel preview-panel" aria-labelledby="preview-heading">
    <div class="panel-heading">
      <h2 id="preview-heading">实时预览</h2>
      <span class="muted">{{ cameraOpen ? `预览中 · ${resolution}` : '摄像头未开启' }}</span>
    </div>
    <div class="preview-stage">
      <img v-show="cameraOpen" ref="image" class="live-image" alt="摄像头实时预览" />
      <div v-if="!cameraOpen" class="preview-empty">
        <div class="viewfinder" aria-hidden="true">
          <i v-for="corner in 4" :key="corner" :class="`corner corner-${corner}`"></i>
          <svg viewBox="0 0 120 140"><path d="M32 127c12-6 19-12 19-23v-8c-12-9-20-27-20-47C31 24 42 10 60 10s29 14 29 39c0 20-8 38-20 47v8c0 11 7 17 19 23" /></svg>
        </div>
        <p>等待摄像头画面</p>
        <span>连接服务并打开摄像头后开始预览</span>
      </div>
    </div>
    <p class="preview-tip" role="status">{{ captureMode === 'auto' ? (autoStatus || '打开摄像头后，人脸稳定约 1.5 秒自动拍一张。') : '请正对摄像头，保持光线均匀。' }}</p>
    <button v-if="captureMode === 'auto'" class="primary capture-button" :disabled="!cameraOpen || !!busy" @click="$emit('retake')">
      {{ autoComplete ? '重新拍照' : '重新检测' }}
    </button>
    <button v-else class="primary capture-button" :disabled="!cameraOpen || !!busy" @click="$emit('capture')">
      {{ busy === '抓拍照片' ? '正在抓拍…' : '抓拍照片' }}
    </button>
  </section>
</template>
