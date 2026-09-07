import { cp, mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { build } from 'vite';

const target = new URL('../public/face-detection/wasm/', import.meta.url);
await mkdir(target, { recursive: true });
await cp(new URL('../node_modules/@mediapipe/tasks-vision/wasm/', import.meta.url), target, { recursive: true });

// Vite's dev server leaves ES imports in classic workers. Bundle once for both
// dev and production so MediaPipe can use importScripts to load its WASM glue.
await build({
  configFile: false,
  publicDir: false,
  build: {
    outDir: fileURLToPath(new URL('../public/face-detection/worker/', import.meta.url)),
    emptyOutDir: false,
    lib: {
      entry: fileURLToPath(new URL('../src/face-detector.worker.js', import.meta.url)),
      name: 'FaceDetectorWorker', formats: ['iife'], fileName: () => 'face-detector.js',
    },
  },
});
