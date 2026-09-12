<script setup lang="ts">
import { ref } from 'vue';
import { FaceCapture, FaceCaptureDialog, type CaptureResult, type FaceCaptureHandle, type CaptureState } from '../../dist/index';
const visible = ref(false);
const capture = ref<FaceCaptureHandle | null>(null);
const state = ref<CaptureState | null>(null);
function receive(result: CaptureResult) {
  if (result.status === 'success') {
    const blob: Blob = result.photo.blob;
    void blob;
  } else if (result.status === 'connection_failed') {
    const code: 'AGENT_CONNECTION_TIMEOUT' = result.code;
    void code;
  }
}
</script>
<template>
  <FaceCaptureDialog v-model="visible" :connection-timeout-ms="60000" @result="receive" />
  <FaceCapture ref="capture" :active="visible" @result="receive" @state-change="state = $event"
    @success="photo => { const blob: Blob = photo.blob; void blob; }"
    @countdown="seconds => { const value: number | null = seconds; void value; }" />
  <button :disabled="!state?.canRetake" @click="capture?.retake()">重拍</button>
</template>
