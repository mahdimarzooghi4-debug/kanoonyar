import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';
import assert from 'node:assert/strict';

const names = ['headquarters', 'center', 'audience'];
for (const name of names) {
  const html = resolve('apps', name, 'index.html');
  const entry = resolve('apps', name, 'main.tsx');
  const outputDir = resolve('dist', name);
  const output = resolve(outputDir, 'index.html');
  assert.ok(existsSync(html), 'missing HTML entry: ' + name);
  assert.ok(existsSync(entry), 'missing React entry: ' + name);
  assert.ok(existsSync(output), 'missing isolated build output: ' + name);
  const source = readFileSync(entry, 'utf8');
  assert.match(source, /createRoot\(/, 'missing React mount: ' + name);
  assert.match(source, /packages\/ui\/Shell/, 'missing shared shell: ' + name);
  const rendered = readFileSync(output, 'utf8');
  assert.match(rendered, /dir="rtl"/);
  assert.match(rendered, /lang="fa"/);
  const assets = resolve(outputDir, 'assets');
  assert.ok(existsSync(assets), 'missing bundled assets: ' + name);
  assert.ok(readdirSync(assets).some(file => file.endsWith('.js')), 'missing JS bundle: ' + name);
  if (name === 'audience') {
    const compiled = readdirSync(assets).filter(file => file.endsWith('.js'))
      .map(file => readFileSync(resolve(assets, file), 'utf8')).join('\n');
    // Even a mistakenly visible manual-token input is not acceptable in
    // the normal public audience artifact. CI harness is built elsewhere.
    assert.ok(!compiled.includes('audience-access-credential'),
      'S54 test-only manual bearer field leaked to public audience deployment');
    assert.ok(!compiled.includes('/api/v1/identity/me'),
      'S54 test-only identity verification leaked to public audience deployment');
  }

}
console.log('Three RTL React entrypoints, shared shell imports and isolated bundles verified');
