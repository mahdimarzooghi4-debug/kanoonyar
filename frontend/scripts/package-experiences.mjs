import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { relative, resolve, sep } from 'node:path';
import assert from 'node:assert/strict';

export const experiences = Object.freeze(['headquarters', 'center', 'audience']);

function filesUnder(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const path = resolve(dir, entry.name);
    return entry.isDirectory() ? filesUnder(path) : entry.isFile() ? [path] : [];
  });
}

export function inventory(root, name) {
  assert.ok(experiences.includes(name), 'unknown experience');
  const dir = resolve(root, 'dist', name);
  assert.ok(existsSync(resolve(dir, 'index.html')), 'missing independently deployable index.html: ' + name);
  const files = filesUnder(dir).sort();
  assert.ok(files.some(file => file.endsWith('.js')), 'missing executable bundle: ' + name);
  return {
    experience: name,
    files: files.map(file => ({
      path: relative(dir, file).split(sep).join('/'),
      bytes: statSync(file).size,
      sha256: createHash('sha256').update(readFileSync(file)).digest('hex'),
    })),
  };
}

export function writeManifests(root) {
  const inventories = experiences.map(name => inventory(root, name));
  const all = new Set();
  for (const item of inventories) {
    for (const file of item.files) {
      assert.ok(!file.path.startsWith('../') && !file.path.startsWith('/'), 'artifact escaped output root');
      all.add(item.experience + '/' + file.path);
    }
  }
  assert.equal(all.size, inventories.reduce((count, item) => count + item.files.length, 0));
  const directory = resolve(root, 'dist', 'manifests');
  mkdirSync(directory, { recursive: true });
  for (const item of inventories) {
    writeFileSync(resolve(directory, item.experience + '.json'), JSON.stringify(item, null, 2) + '\n');
  }
  return inventories;
}

if (process.argv[1] && resolve(process.argv[1]) === resolve(import.meta.filename ?? '')) {
  const root = resolve(import.meta.dirname, '..');
  const manifests = writeManifests(root);
  console.log('Verified separate static deployment inventories: ' + manifests.map(item => item.experience).join(', '));
}
