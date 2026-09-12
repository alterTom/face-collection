<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref, useId, watch } from 'vue';
import FaceCapture from './FaceCapture.vue';
const props = defineProps({
  modelValue: { type: Boolean, default: false },
  serviceUrl: { type: String, default: 'ws://127.0.0.1:17653/face' },
  deviceId: { type: String, default: undefined },
  stableDurationMs: { type: Number, default: 1500 },
  connectionTimeoutMs: { type: Number, default: 60_000 },
  title: { type: String, default: '人脸采集' },
});
const emit = defineEmits(['update:modelValue', 'result']);
const dialog = ref(null), capture = ref(null), captureActive = ref(false);
const state = ref({ phase: 'idle', message: '', remainingSeconds: null });
const titleId = useId(), statusId = useId();
const canRetake = computed(() => ['capturing', 'error'].includes(state.value.phase));
let mounted = false, opening = 0, stopWatch, opened = false;
function receive(result) {
  if (!opened) return;
  opened = false;
  captureActive.value = false;
  dialog.value?.close();
  emit('update:modelValue', false);
  emit('result', result);
}
function cancel() {
  capture.value?.cancel();
  // Closing the shell must not depend on the child's async session startup.
  if (opened) receive({ status: 'cancelled' });
}
function retake() { return capture.value?.retake(); }
onMounted(() => {
  mounted = true;
  stopWatch = watch(() => props.modelValue, async visible => {
    const generation = ++opening;
    if (!visible) { captureActive.value = false; cancel(); return; }
    await nextTick();
    if (!mounted || !props.modelValue || generation !== opening) return;
    opened = true;
    dialog.value.showModal();
    captureActive.value = true;
  }, { immediate: true, flush: 'post' });
});
onBeforeUnmount(() => { mounted = false; opening++; stopWatch?.(); cancel(); });
</script>

<template>
  <Teleport to="body">
    <dialog ref="dialog" class="fcw-dialog" :aria-labelledby="titleId" :aria-describedby="statusId" @cancel.prevent="cancel">
      <div class="fcw-header">
        <h2 :id="titleId">{{ title }}</h2>
        <button class="fcw-close" type="button" aria-label="关闭人脸采集" @click="cancel">×</button>
      </div>
      <div class="fcw-content">
        <FaceCapture ref="capture" :active="captureActive" :service-url="serviceUrl" :device-id="deviceId"
          :stable-duration-ms="stableDurationMs" :connection-timeout-ms="connectionTimeoutMs"
          @state-change="state = $event" @result="receive" />
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
