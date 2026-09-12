<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref, useId, watch } from 'vue';
import { createCaptureSession } from './session.js';

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  serviceUrl: { type: String, default: 'ws://127.0.0.1:17653/face' },
  deviceId: { type: String, default: undefined },
  stableDurationMs: { type: Number, default: 1500 },
  connectionTimeoutMs: { type: Number, default: 60_000 },
  title: { type: String, default: '人脸采集' },
});
const emit = defineEmits(['update:modelValue', 'result']);
const dialog = ref(null), preview = ref(null);
const state = ref({ phase: 'idle', message: '', remainingSeconds: null });
const titleId = useId(), statusId = useId();
const canRetake = computed(() => ['capturing', 'error'].includes(state.value.phase));
let session, stopWatch, mounted = false, opening = 0;

function cancel() { session?.cancel(); }
function retake() { void session?.retake(); }
async function open() {
  const generation = ++opening;
  await nextTick();
  if (!mounted || !props.modelValue || generation !== opening || session) return;
  state.value = { phase: 'idle', message: '', remainingSeconds: null };
  dialog.value.showModal();
  const current = createCaptureSession({ serviceUrl: props.serviceUrl, deviceId: props.deviceId,
    stableDurationMs: props.stableDurationMs, connectionTimeoutMs: props.connectionTimeoutMs }, {
    onState: value => { state.value = value; },
    onResult: result => {
      if (session !== current) return;
      session = undefined;
      // Close the native modal synchronously before delivering photos to the host.
      dialog.value?.close();
      emit('update:modelValue', false);
      emit('result', result);
    },
  });
  session = current;
  current.start(preview.value);
}
onMounted(() => {
  mounted = true;
  window.addEventListener('pagehide', cancel);
  stopWatch = watch(() => props.modelValue, visible => {
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
  <Teleport to="body">
    <dialog ref="dialog" class="fcw-dialog" :aria-labelledby="titleId" :aria-describedby="statusId" @cancel.prevent="cancel">
      <div class="fcw-header">
        <h2 :id="titleId">{{ title }}</h2>
        <button class="fcw-close" type="button" aria-label="关闭人脸采集" @click="cancel">×</button>
      </div>
      <div class="fcw-content">
        <div class="fcw-viewfinder" :class="{ 'fcw-viewfinder--error': state.phase === 'error' }">
          <div class="fcw-placeholder" aria-hidden="true">
            <svg viewBox="0 0 240 240" fill="none">
              <path d="M83 97c-5-23-2-48 37-48s42 25 37 48c11 2 7 25-2 28-3 17-11 25-16 31v15c0 12 31 16 47 29-32 18-100 18-132 0 16-13 47-17 47-29v-15c-5-6-13-14-16-31-9-3-13-26-2-28Z" />
            </svg>
          </div>
          <img ref="preview" class="fcw-preview" alt="摄像头实时预览" />
          <div class="fcw-face-guide" aria-hidden="true"></div>
        </div>
        <p :id="statusId" class="fcw-status" :class="{ 'fcw-status--error': state.phase === 'error' }" role="status" aria-live="polite">{{ state.message }}</p>
        <p class="fcw-hint">{{ state.phase === 'connecting' ? '请确认本机采集服务已启动' : '面向摄像头，单张人脸稳定后将自动完成采集' }}</p>
        <p v-if="state.remainingSeconds !== null" class="fcw-countdown" role="timer">连接倒计时 <strong>{{ state.remainingSeconds }}</strong> 秒</p>
        <div v-else class="fcw-countdown fcw-countdown--empty" aria-hidden="true"></div>
        <div class="fcw-actions">
          <button class="fcw-button fcw-retake" type="button" :disabled="!canRetake" @click="retake">重新拍照</button>
          <button class="fcw-button fcw-exit" type="button" autofocus @click="cancel">退出认证</button>
        </div>
      </div>
    </dialog>
  </Teleport>
</template>

<style scoped>
.fcw-dialog { --fcw-primary: #465dce; box-sizing: border-box; position: fixed; inset: 0; margin: auto; padding: 0; border: 0; width: min(580px, calc(100vw - 32px)); max-width: none; max-height: calc(100dvh - 32px); border-radius: 18px; background: #fff; color: #26314c; font: 15px/1.6 "Segoe UI", "Microsoft YaHei", sans-serif; box-shadow: 0 24px 90px #18245033; overflow: auto; }
.fcw-dialog *, .fcw-dialog *::before, .fcw-dialog *::after { box-sizing: border-box; }
.fcw-dialog::backdrop { background: #18213885; backdrop-filter: blur(3px); }
.fcw-header { display: flex; align-items: center; justify-content: space-between; padding: 18px 24px; background: var(--fcw-primary); color: white; }
.fcw-header h2 { margin: 0; font-size: 21px; font-weight: 650; line-height: 1.5; }
.fcw-close { display: grid; place-items: center; width: 36px; height: 36px; border: 0; border-radius: 50%; padding: 0; background: #ffffff22; color: white; font: 30px/1 sans-serif; cursor: pointer; }
.fcw-content { padding: 32px 32px 36px; text-align: center; }
.fcw-viewfinder { position: relative; width: min(100%, 340px); aspect-ratio: 1; margin: 0 auto 24px; border: 5px solid #69e6eb; border-bottom-color: #4e71ed; border-right-color: #9eefee; border-radius: 50%; overflow: hidden; background: #f3f6ff; }
.fcw-viewfinder--error { border-color: #f2a8ac; }
.fcw-placeholder, .fcw-preview { position: absolute; inset: 0; width: 100%; height: 100%; }
.fcw-placeholder { display: grid; place-items: center; background-image: linear-gradient(#dfe7ff88 1px, transparent 1px), linear-gradient(90deg, #dfe7ff88 1px, transparent 1px); background-size: 24px 24px; }
.fcw-placeholder svg { width: 86%; stroke: #5c7bf0; stroke-width: 3; fill: #cedaffaa; }
.fcw-preview { object-fit: cover; transform: scaleX(-1); }
.fcw-preview:not([src]) { visibility: hidden; }
.fcw-face-guide { position: absolute; left: 25%; top: 14%; width: 50%; height: 65%; border: 1px dashed #ffffff88; border-radius: 48%; pointer-events: none; }
.fcw-status { margin: 0; min-height: 26px; font-weight: 600; font-size: 16px; overflow-wrap: anywhere; }
.fcw-status--error { color: #ce3e4b; }
.fcw-hint { margin: 8px 0 0; font-size: 13px; color: #737d92; }
.fcw-countdown { height: 30px; margin: 12px 0 16px; color: #737d92; font-size: 13px; }
.fcw-countdown strong { font-size: 19px; color: var(--fcw-primary); font-variant-numeric: tabular-nums; }
.fcw-actions { display: flex; gap: 14px; max-width: 390px; margin: auto; }
.fcw-button { flex: 1; min-width: 0; min-height: 48px; padding: 10px 16px; border: 0; border-radius: 9px; color: #fff; font: inherit; font-weight: 600; cursor: pointer; }
.fcw-retake { background: #626cf2; }
.fcw-exit { background: #e35460; }
.fcw-button:hover:not(:disabled), .fcw-close:hover { filter: brightness(.94); }
.fcw-button:disabled { background: #e5e9f5; color: #8a94ad; cursor: not-allowed; }
.fcw-dialog button:focus-visible { outline: 3px solid #152c8e; outline-offset: 3px; }
@media (max-width: 480px) { .fcw-dialog { width: calc(100vw - 24px); border-radius: 14px; } .fcw-header { padding: 14px 18px; } .fcw-content { padding: 24px 20px; } .fcw-viewfinder { width: min(100%, 280px); } .fcw-actions { gap: 10px; } .fcw-button { padding-inline: 8px; } }
@media (max-height: 650px) and (min-width: 481px) { .fcw-content { padding-block: 20px; } .fcw-viewfinder { width: 230px; margin-bottom: 16px; } }
</style>
