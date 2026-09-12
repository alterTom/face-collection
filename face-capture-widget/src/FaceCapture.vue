<script setup>
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { createCaptureSession } from './session.js';

const props = defineProps({
  active: { type: Boolean, default: false },
  serviceUrl: { type: String, default: 'ws://127.0.0.1:17653/face' },
  deviceId: { type: String, default: undefined },
  stableDurationMs: { type: Number, default: 1500 },
  connectionTimeoutMs: { type: Number, default: 60_000 },
});
const emit = defineEmits(['result', 'success', 'connection-failed', 'error', 'cancelled', 'state-change', 'countdown']);
const preview = ref(null);
let remainingSeconds;
const state = ref({ phase: 'idle', message: '', remainingSeconds: null });
let session, stopWatch, mounted = false, opening = 0, pending = false;

function publishState(value) {
  state.value = value;
  emit('state-change', { ...value, canRetake: ['capturing', 'error'].includes(value.phase) });
  if (remainingSeconds !== value.remainingSeconds) {
    remainingSeconds = value.remainingSeconds;
    emit('countdown', remainingSeconds);
  }
}
function publishResult(result) {
  emit('result', result);
  if (result.status === 'success') emit('success', result.photo);
  else if (result.status === 'connection_failed') emit('connection-failed', result);
  else if (result.status === 'error') emit('error', result);
  else emit('cancelled');
}
function cancel() {
  opening++;
  if (session) session.cancel();
  else if (pending) {
    pending = false;
    publishState({ phase: 'closed', message: '', remainingSeconds: null });
    publishResult({ status: 'cancelled' });
  }
}
function retake() { return session?.retake(); }
defineExpose({ retake, cancel });
async function open() {
  const generation = ++opening;
  pending = true;
  await nextTick();
  if (!mounted || !props.active || generation !== opening || session) return;
  pending = false;
  state.value = { phase: 'idle', message: '', remainingSeconds: null };

  const current = createCaptureSession({ serviceUrl: props.serviceUrl, deviceId: props.deviceId,
    stableDurationMs: props.stableDurationMs, connectionTimeoutMs: props.connectionTimeoutMs }, {
    onState: publishState,
    onResult: result => {
      if (session !== current) return;
      session = undefined;
      publishResult(result);
    },
  });
  session = current;
  current.start(preview.value);
}
onMounted(() => {
  mounted = true;
  window.addEventListener('pagehide', cancel);
  stopWatch = watch(() => props.active, visible => {
    if (visible) void open();
    else { opening++; cancel(); }
  }, { immediate: true, flush: 'post' });
});
onBeforeUnmount(() => {
  mounted = false; opening++; stopWatch?.();
  window.removeEventListener('pagehide', cancel);
  cancel();
});
</script>

<template>
  <div class="fcw-viewfinder" role="group" aria-label="人脸取景框"
    :class="{ 'fcw-viewfinder--error': state.phase === 'error', 'fcw-viewfinder--connecting': state.phase === 'connecting' }"
    :aria-busy="state.phase === 'connecting'">
    <div class="fcw-placeholder" aria-hidden="true">
      <svg viewBox="0 0 240 240" fill="none">
        <path d="M83 97c-5-23-2-48 37-48s42 25 37 48c11 2 7 25-2 28-3 17-11 25-16 31v15c0 12 31 16 47 29-32 18-100 18-132 0 16-13 47-17 47-29v-15c-5-6-13-14-16-31-9-3-13-26-2-28Z" />
      </svg>
    </div>
    <img ref="preview" class="fcw-preview" alt="摄像头实时预览" />
    <div class="fcw-face-guide" aria-hidden="true"></div>
  </div>
</template>

<style scoped>
.fcw-viewfinder, .fcw-viewfinder::after { box-sizing: border-box; }
.fcw-viewfinder { position: relative; width: min(100%, 340px); aspect-ratio: 1; margin: 0 auto 24px; border-radius: 50%; overflow: hidden; background: #f3f6ff; }
.fcw-viewfinder::after { content: ""; position: absolute; inset: 0; border: 5px solid #69e6eb; border-bottom-color: #4e71ed; border-right-color: #9eefee; border-radius: 50%; pointer-events: none; }
.fcw-viewfinder--connecting::after { animation: fcw-connecting-spin 1.2s linear infinite; }
.fcw-viewfinder--error::after { border-color: #f2a8ac; }
@keyframes fcw-connecting-spin { to { transform: rotate(360deg); } }
@media (prefers-reduced-motion: reduce) { .fcw-viewfinder--connecting::after { animation: none; } }
.fcw-placeholder, .fcw-preview { position: absolute; inset: 5px; width: calc(100% - 10px); height: calc(100% - 10px); border-radius: 50%; }
.fcw-placeholder { display: grid; place-items: center; background-image: linear-gradient(#dfe7ff88 1px, transparent 1px), linear-gradient(90deg, #dfe7ff88 1px, transparent 1px); background-size: 24px 24px; }
.fcw-placeholder svg { width: 86%; stroke: #5c7bf0; stroke-width: 3; fill: #cedaffaa; }
.fcw-preview { object-fit: cover; transform: scaleX(-1); max-width: none; max-height: none; margin: 0; }
.fcw-preview:not([src]) { visibility: hidden; }
.fcw-face-guide { position: absolute; left: 25%; top: 14%; width: 50%; height: 65%; border: 1px dashed #ffffff88; border-radius: 48%; pointer-events: none; }
</style>
