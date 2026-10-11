import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import assert from 'node:assert/strict';
const names = ['headquarters', 'center', 'audience'];
for (const name of names) {
  const html = resolve('apps', name, 'index.html');
  const entry = resolve('apps', name, 'main.tsx');
  const output = resolve('dist', name, 'index.html');
  assert.ok(existsSync(html), 'missing HTML entry: ' + name);
  assert.ok(existsSync(entry), 'missing React entry: ' + name);
  assert.ok(existsSync(output), 'missing isolated build output: ' + name);
  const text = readFileSync(output, 'utf8');
  assert.match(text, /dir="rtl"/);
  assert.match(text, /lang="fa"/);
}
console.log('All three RTL experience entrypoints and isolated build outputs verified');
