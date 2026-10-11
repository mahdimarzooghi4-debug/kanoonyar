import { defineConfig } from 'vite';
import { resolve } from 'node:path';
export default defineConfig({
  build: {
    rollupOptions: {
      input: {
        headquarters: resolve(__dirname, 'apps/headquarters/index.html'),
        center: resolve(__dirname, 'apps/center/index.html'),
        audience: resolve(__dirname, 'apps/audience/index.html'),
      },
    },
  },
});
