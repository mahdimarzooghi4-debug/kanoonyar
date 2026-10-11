import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';
import { test } from 'node:test';
import { inventory, writeManifests, experiences } from './package-experiences.mjs';

function withFixture(fn) {
  const root = mkdtempSync(resolve(tmpdir(), 'kanoonyar-deploy-'));
  try {
    for (const name of experiences) {
      const dir = resolve(root, 'dist', name);
      mkdirSync(dir, { recursive: true });
      writeFileSync(resolve(dir, 'index.html'), '<html lang="fa" dir="rtl"></html>');
      writeFileSync(resolve(dir, 'app.js'), 'console.log("test")');
    }
    fn(root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

test('three independent inventories contain only their own files and hashes', () => {
  withFixture(root => {
    const results = writeManifests(root);
    assert.equal(results.length, 3);
    for (const name of experiences) {
      const manifest = JSON.parse(readFileSync(resolve(root, 'dist', 'manifests', name + '.json'), 'utf8'));
      assert.equal(manifest.experience, name);
      assert.deepEqual(manifest.files.map(file => file.path), ['app.js', 'index.html']);
      assert.match(manifest.files[0].sha256, /^[0-9a-f]{64}$/);
    }
  });
});

test('missing independent bundle fails closed', () => {
  withFixture(root => {
    rmSync(resolve(root, 'dist', 'center', 'app.js'));
    assert.throws(() => inventory(root, 'center'), /missing executable bundle/);
  });
});

test('unknown experience is rejected', () => {
  withFixture(root => assert.throws(() => inventory(root, 'admin'), /unknown experience/));
});
