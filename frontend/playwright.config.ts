import { defineConfig } from '@playwright/test';

const experience = process.env.KANOONYAR_BROWSER_EXPERIENCE ?? 'headquarters';
if (!['headquarters', 'center', 'audience'].includes(experience)) {
  throw new Error('Unsupported browser experience');
}

export default defineConfig({
  testDir: './tests/browser',
  use: { baseURL: 'http://127.0.0.1:4173', headless: true },
  webServer: {
    command: 'npm run preview:' + experience,
    url: 'http://127.0.0.1:4173',
    reuseExistingServer: false,
    env: { KANOONYAR_EXPERIENCE: experience,
      KANOONYAR_AUDIENCE_TEST_BUNDLE: experience === 'audience' ? '1' : '0' },
    timeout: 30000,
  },
});
