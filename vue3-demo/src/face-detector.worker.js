import { FaceDetector, FilesetResolver } from '@mediapipe/tasks-vision';

let detector;
self.onmessage = async ({ data }) => {
  try {
    if (data.type === 'init') {
      const files = await FilesetResolver.forVisionTasks(`${data.assetRoot}wasm`);
      detector = await FaceDetector.createFromOptions(files, {
        baseOptions: { modelAssetPath: `${data.assetRoot}blaze_face_short_range.tflite`, delegate: 'CPU' },
        runningMode: 'IMAGE', minDetectionConfidence: 0.7,
      });
      self.postMessage({ type: 'ready' });
    } else if (data.type === 'detect') {
      const { width, height } = data.bitmap;
      const result = detector.detect(data.bitmap);
      self.postMessage({ type: 'result', faces: result.detections.map(({ boundingBox: b }) => ({
        x: b.originX / width, y: b.originY / height, width: b.width / width, height: b.height / height,
      })) });
    }
  } catch {
    self.postMessage({ type: 'error' });
  } finally {
    data.bitmap?.close();
  }
};
