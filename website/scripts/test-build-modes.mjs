import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtemp, readFile, mkdir, writeFile, rm } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
import { validateOutput } from './quality-gate.mjs';

const root = fileURLToPath(new URL('../', import.meta.url));
const parent = resolve(process.env.SITE_TEST_OUTPUT_ROOT || resolve(root, 'test-results'), 'mode-tests');
await mkdir(parent, { recursive: true });
const work = await mkdtemp(resolve(parent, 'run-'));
const scenarios = [];
const cases = [
  ['private-preview', 'complete', undefined, true],
  ['private-preview', 'complete', 'synthetic-test-verification-value', true],
  ['private-preview', 'fallback', undefined, true],
  ['private-preview', 'no-stable', undefined, true],
  ['private-preview', 'incomplete', undefined, false],
  ['private-preview', 'malformed', undefined, false],
  ['release-candidate', 'fallback', undefined, false],
  ['release-candidate', 'no-stable', undefined, false],
  ['release-candidate', 'incomplete', undefined, false],
];
for (const [mode, scenario, verification, success] of cases) {
  const output = resolve(work, `${mode}-${scenario}-${scenarios.length}`);
  const env = { ...process.env, SITE_BUILD_MODE: mode, SITE_TEST_SCENARIO: scenario,
    NODE_OPTIONS: `--import=${new URL('../tests/fixtures/build-hook.mjs', import.meta.url).href}` };
  delete env.GOOGLE_SITE_VERIFICATION;
  delete env.SITE_TEST_FIXTURE;
  if (verification) env.GOOGLE_SITE_VERIFICATION = verification;
  if (mode === 'private-preview') env.SITE_TEST_FIXTURE = 'synthetic-release';
  let log = '';
  const child = spawn(process.execPath, ['scripts/astro.mjs', 'build', '--outDir', output],
    { cwd: root, env, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  for (const stream of [child.stdout, child.stderr]) stream.on('data', data => { log += data; });
  const exitCode = await new Promise((done, fail) => { child.on('error', fail); child.on('exit', done); });
  assert.equal(exitCode === 0, success, log);
  if (success) {
    await validateOutput(output, mode, verification);
    const html = await readFile(resolve(output, 'index.html'), 'utf8');
    assert.match(html, /Synthetic release data/);
    if (scenario === 'complete') assert.match(html, /"softwareVersion":"2\.4\.7"/);
    else assert.doesNotMatch(html, /softwareVersion|downloadUrl|class="package-row"/);
  } else assert.match(log, /Release data:/, 'Must fail on provider evidence');
  scenarios.push({ mode, scenario, verificationConfigured: !!verification, exitCode, result: 'PASS' });
}
await writeFile(resolve(parent, 'results.json'), JSON.stringify({ result: 'PASS', scenarios }, null, 2) + '\n');
// Only this unique, known synthetic output belongs to this run. Measure before deletion.
async function measure(directory) {
  const { readdir, stat } = await import('node:fs/promises');
  let files = 0, bytes = 0;
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const path = resolve(directory, entry.name);
    if (entry.isSymbolicLink()) throw new Error('Unexpected link in owned build fixtures');
    if (entry.isDirectory()) { const child = await measure(path); files += child.files; bytes += child.bytes; }
    else { files++; bytes += (await stat(path)).size; }
  }
  return { files, bytes };
}
assert.ok(work.startsWith(parent + (process.platform === 'win32' ? '\\' : '/')));
const inventory = await measure(work);
await rm(work, { recursive: true });
await writeFile(resolve(parent, 'cleanup.json'), JSON.stringify({ classification: 'task-owned reproducible synthetic builds', ...inventory, removed: true }, null, 2) + '\n');
console.log(JSON.stringify({ result: 'PASS', scenarios }));
