# Face detection assets

- Runtime: `@mediapipe/tasks-vision` 0.10.32 (Apache-2.0), pinned in package-lock.json.
- Model: Google MediaPipe BlazeFace short-range, float16, version 1.
- Source: https://storage.googleapis.com/mediapipe-models/face_detector/blaze_face_short_range/float16/1/blaze_face_short_range.tflite
- Documentation: https://ai.google.dev/edge/mediapipe/solutions/vision/face_detector
- SHA-256: `b4578f35940bf5a1a655214a1cce5cab13eba73c1297cd78e1a04c2380b0152f`

The model is a binary build input. `scripts/prepare-face-assets.mjs` copies WASM assets from the installed npm package and bundles a classic worker before development/build; generated `wasm/` and `worker/` directories are ignored by Git. After editing worker source, rerun this script or restart `npm run dev`. Deploy all of `dist/` together; inference needs no third-party network requests.
