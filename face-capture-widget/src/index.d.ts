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

export interface FaceCaptureDialogProps {
  modelValue?: boolean;
  serviceUrl?: string;
  deviceId?: string;
  stableDurationMs?: number;
  connectionTimeoutMs?: number;
  title?: string;
}
export declare const FaceCaptureDialog: DefineComponent<FaceCaptureDialogProps & {
  'onUpdate:modelValue'?: (visible: boolean) => void;
  onResult?: (result: CaptureResult) => void;
}>;
