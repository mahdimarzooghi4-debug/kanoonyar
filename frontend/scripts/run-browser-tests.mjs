import { spawnSync } from 'node:child_process';
for (const experience of ['headquarters', 'center', 'audience']) {
  // The browser test-only masked token UI must NEVER enter dist/audience.
  // A distinct CI-only build retains S54 negative-auth browser coverage.
  if (experience === 'audience') {
    const isolated = spawnSync('npm', ['run', 'build:audience:ci'], {
      stdio: 'inherit',
      shell: process.platform === 'win32',
      env: { ...process.env },
    });
    if (isolated.status !== 0) process.exit(isolated.status ?? 1);
  }
  const result = spawnSync('npx', ['playwright', 'test'], {
    stdio: 'inherit',
    shell: process.platform === 'win32',
    env: { ...process.env, KANOONYAR_BROWSER_EXPERIENCE: experience },
  });
  if (result.status !== 0) process.exit(result.status ?? 1);
}
