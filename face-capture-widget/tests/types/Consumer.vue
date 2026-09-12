<script setup lang="ts">
import { ref } from 'vue';
import { FaceCaptureDialog, type CaptureResult } from '../../dist/index';
const visible = ref(false);
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
<template><FaceCaptureDialog v-model="visible" :connection-timeout-ms="60000" @result="receive" /></template>
