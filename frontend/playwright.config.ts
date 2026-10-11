import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/browser',
  use: { baseURL: 'http://127.0.0.1:4173', headless: true },
  webServer: {
    command: 'npm run preview:headquarters',
    url: 'http://127.0.0.1:4173',
    reuseExistingServer: false,
    timeout: 30000,
  },
});
