import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync, statSync } from 'node:fs';
import { resolve, sep } from 'node:path';
import { experiences } from './package-experiences.mjs';

export function verifyManifest(root, name) {
  assert.ok(experiences.includes(name), 'unknown experience');
  const manifestPath = resolve(root, 'dist', 'manifests', name + '.json');
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
  assert.equal(manifest.experience, name, 'manifest experience mismatch');
  assert.ok(Array.isArray(manifest.files) && manifest.files.length > 0, 'empty manifest');
  const directory = resolve(root, 'dist', name);
  const seen = new Set();
  for (const file of manifest.files) {
    assert.equal(typeof file.path, 'string');
    assert.ok(file.path && !file.path.startsWith('/') && !file.path.includes('\\') && !file.path.split('/').includes('..') && !file.path.split('/').includes('.'), 'unsafe artifact path');
    assert.ok(!seen.has(file.path), 'duplicate artifact path');
    seen.add(file.path);
    const path = resolve(directory, file.path);
    assert.ok(path.startsWith(directory + sep), 'artifact escaped experience root');
    assert.ok(existsSync(path) && statSync(path).isFile(), 'artifact missing: ' + file.path);
    const contents = readFileSync(path);
    assert.equal(contents.length, file.bytes, 'artifact size mismatch: ' + file.path);
    assert.equal(createHash('sha256').update(contents).digest('hex'), file.sha256, 'artifact digest mismatch: ' + file.path);
  }
  assert.ok(seen.has('index.html'), 'missing entry document');
  return manifest.files.length;
}

if (process.argv[1] && resolve(process.argv[1]) === resolve(import.meta.filename ?? '')) {
  const root = resolve(import.meta.dirname, '..');
  for (const name of experiences) console.log(name + ': verified ' + verifyManifest(root, name) + ' files');
}
