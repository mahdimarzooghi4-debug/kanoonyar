import { defineConfig } from 'vite';
import { resolve } from 'node:path';

const experiences = ['headquarters', 'center', 'audience'] as const;
const requested = process.env.KANOONYAR_EXPERIENCE;
if (!requested || !experiences.some(name => name === requested)) {
  throw new Error('KANOONYAR_EXPERIENCE must be headquarters, center, or audience');
}

export default defineConfig({
  root: resolve(__dirname, 'apps', requested),
  build: {
    outDir: resolve(__dirname, 'dist', requested),
    emptyOutDir: true,
  },
});
