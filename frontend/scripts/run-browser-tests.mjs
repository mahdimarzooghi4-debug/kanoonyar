import { spawnSync } from 'node:child_process';
for (const experience of ['headquarters', 'center', 'audience']) {
  const result = spawnSync('npx', ['playwright', 'test'], {
    stdio: 'inherit',
    shell: process.platform === 'win32',
    env: { ...process.env, KANOONYAR_BROWSER_EXPERIENCE: experience },
  });
  if (result.status !== 0) process.exit(result.status ?? 1);
}
