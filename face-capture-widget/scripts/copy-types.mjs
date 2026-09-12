import { copyFile } from 'node:fs/promises';
await copyFile(new URL('../src/index.d.ts', import.meta.url), new URL('../dist/index.d.ts', import.meta.url));
