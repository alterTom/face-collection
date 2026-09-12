import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import { fileURLToPath } from 'node:url';

export default defineConfig({
  plugins: [vue()],
  server: { fs: { allow: [fileURLToPath(new URL('..', import.meta.url))] } },
  build: {
    lib: { entry: fileURLToPath(new URL('./src/index.js', import.meta.url)),
      formats: ['es'], fileName: 'face-capture-vue', cssFileName: 'face-capture-vue' },
    rollupOptions: { external: ['vue'] },
  },
});
