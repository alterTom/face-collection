import type { DefineComponent } from 'vue';

export interface CapturePhoto {
  base64: string;
  blob: Blob;
  mimeType: 'image/jpeg';
  width: number;
  height: number;
  size: number;
  capturedAt?: string;
}
export type CaptureResult =
  | { status: 'success'; photo: CapturePhoto }
  | { status: 'cancelled' }
  | { status: 'connection_failed'; code: 'AGENT_CONNECTION_TIMEOUT'; message: string }
  | { status: 'error'; code: 'INVALID_OPTIONS'; message: string };

export interface CaptureOptions {
  serviceUrl?: string;
  deviceId?: string;
  stableDurationMs?: number;
  connectionTimeoutMs?: number;
}
export interface FaceCaptureDialogProps extends CaptureOptions {
  modelValue?: boolean;
  title?: string;
}
export interface FaceCaptureProps extends CaptureOptions { active?: boolean }
export interface CaptureState {
  phase: 'idle' | 'connecting' | 'opening' | 'capturing' | 'rearming' | 'error' | 'closed';
  message: string;
  remainingSeconds: number | null;
  canRetake: boolean;
}
export interface FaceCaptureHandle {
  retake(): Promise<void> | undefined;
  cancel(): void;
}
export declare const FaceCapture: DefineComponent<FaceCaptureProps & {
  onResult?: (result: CaptureResult) => void;
  onSuccess?: (photo: CapturePhoto) => void;
  onConnectionFailed?: (result: Extract<CaptureResult, { status: 'connection_failed' }>) => void;
  onError?: (result: Extract<CaptureResult, { status: 'error' }>) => void;
  onCancelled?: () => void;
  onStateChange?: (state: CaptureState) => void;
  onCountdown?: (seconds: number | null) => void;
}, FaceCaptureHandle>;
export declare const FaceCaptureDialog: DefineComponent<FaceCaptureDialogProps & {
  'onUpdate:modelValue'?: (visible: boolean) => void;
  onResult?: (result: CaptureResult) => void;
}>;
