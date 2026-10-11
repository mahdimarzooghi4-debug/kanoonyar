import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';
import { test } from 'node:test';
import { experiences, writeManifests } from './package-experiences.mjs';
import { verifyManifest } from './verify-deployment.mjs';

function fixture(fn) {
  const root = mkdtempSync(resolve(tmpdir(), 'kanoonyar-verify-'));
  try {
    for (const name of experiences) {
      const dir = resolve(root, 'dist', name);
      mkdirSync(dir, { recursive: true });
      writeFileSync(resolve(dir, 'index.html'), '<html lang="fa" dir="rtl"></html>');
      writeFileSync(resolve(dir, 'app.js'), 'console.log("ok")');
    }
    writeManifests(root);
    fn(root);
  } finally { rmSync(root, { recursive: true, force: true }); }
}

test('verifies all three unmodified release bundles', () => fixture(root => {
  for (const name of experiences) assert.equal(verifyManifest(root, name), 2);
}));

test('tampered artifact is rejected', () => fixture(root => {
  writeFileSync(resolve(root, 'dist', 'center', 'app.js'), 'console.log("bad")');
  assert.throws(() => verifyManifest(root, 'center'), /mismatch/);
}));

test('cross-experience manifest is rejected', () => fixture(root => {
  const path = resolve(root, 'dist', 'manifests', 'center.json');
  const data = JSON.parse(readFileSync(path, 'utf8'));
  data.experience = 'headquarters';
  writeFileSync(path, JSON.stringify(data));
  assert.throws(() => verifyManifest(root, 'center'), /experience mismatch/);
}));

test('path traversal in manifest is rejected', () => fixture(root => {
  const path = resolve(root, 'dist', 'manifests', 'audience.json');
  const data = JSON.parse(readFileSync(path, 'utf8'));
  data.files[0].path = '../center/app.js';
  writeFileSync(path, JSON.stringify(data));
  assert.throws(() => verifyManifest(root, 'audience'), /unsafe artifact path/);
}));

test('duplicate manifest entries are rejected', () => fixture(root => {
  const path = resolve(root, 'dist', 'manifests', 'audience.json');
  const data = JSON.parse(readFileSync(path, 'utf8'));
  data.files.push(data.files[0]);
  writeFileSync(path, JSON.stringify(data));
  assert.throws(() => verifyManifest(root, 'audience'), /duplicate artifact path/);
}));
