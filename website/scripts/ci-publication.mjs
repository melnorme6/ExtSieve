import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { appendFile, lstat, readdir, readFile, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { compareVersions, loadRelease } from '../src/lib/releases.ts';
import { validateOutput } from './quality-gate.mjs';

export const publicRepository = 'melnorme6/ExtSieve';
const api = `https://api.github.com/repos/${publicRepository}`;
const workflowPath = '.github/workflows/website.yml';
const provenanceUrl = 'https://melnorme6.github.io/ExtSieve/build-provenance.json';
const sha = /^[a-f0-9]{40}$/;
const digest = /^[a-f0-9]{64}$/;
const stableTag = /^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/;
const fail = message => { throw new Error(`Publication guard: ${message}`); };
const hash = value => createHash('sha256').update(value).digest('hex');
const integer = value => Number.isSafeInteger(value) && value > 0;

export function publicationAllowed(input, event) {
  if (input.eventName === 'pull_request') return false;
  if (!['', 'disabled', 'initial', 'enabled'].includes(input.policy ?? '')) fail('Unknown deployment policy.');
  if (!['initial', 'enabled'].includes(input.policy)) return false;
  if (input.repository !== publicRepository) return false;
  if (event.repository?.full_name !== publicRepository || event.repository.private !== false || event.repository.default_branch !== 'main') fail('Expected public repository/main is unavailable.');
  if (input.eventName === 'push' || input.eventName === 'workflow_dispatch') {
    if (input.ref !== 'refs/heads/main') fail('Only main can publish.');
    if (input.eventName === 'workflow_dispatch' && !stableTag.test(event.inputs?.release_tag ?? '')) fail('Manual retry requires a canonical stable release tag.');
    return true;
  }
  if (input.eventName === 'release') {
    if (event.action !== 'published' || event.release?.draft !== false || event.release?.prerelease !== false) return false;
    if (!integer(event.release.id) || !stableTag.test(event.release.tag_name)) fail('Invalid stable release event.');
    return true;
  }
  fail('Unsupported publication event.');
}

// Bounded public reads only. Redirects, outages and malformed responses fail closed.
export async function publicJson(url, allow404 = false, request = fetch) {
  const response = await request(url, {
    redirect: 'error', cache: 'no-store', signal: AbortSignal.timeout(8000),
    headers: { Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2026-03-10' },
  });
  if (response.status === 404 && allow404) return undefined;
  if (!response.ok) fail(`Public metadata request failed (${response.status}).`);
  const reader = response.body?.getReader();
  if (!reader) fail('Empty metadata response.');
  const chunks = [];
  let size = 0;
  try {
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      size += value.byteLength;
      if (size > 1024 * 1024) fail('Metadata response exceeds limit.');
      chunks.push(value);
    }
    return JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(Buffer.concat(chunks)));
  } finally { await reader.cancel(); }
}

export function validateProvenance(value) {
  if (!value || value.schema !== 1 || !sha.test(value.sourceSha) || !sha.test(value.workflowSha) ||
      !integer(value.releaseId) || !stableTag.test(value.releaseTag) || value.releaseTag !== `v${value.version}` ||
      !digest.test(value.releaseFingerprint) || !digest.test(value.filesDigest) ||
      !integer(value.runId) || !integer(value.runNumber) || !integer(value.runAttempt)) fail('Invalid deployed provenance.');
  compareVersions(value.version, value.version);
  return value;
}

export async function preparePublication(input, event, dependencies = {}) {
  if (!publicationAllowed(input, event)) fail('Publication is disabled for this event.');
  if (!sha.test(input.sourceSha) || !sha.test(input.workflowSha) ||
      !input.workflowRef?.startsWith(`${publicRepository}/${workflowPath}@`)) fail('Invalid source/workflow provenance.');
  if (!integer(input.runId) || !integer(input.runNumber) || !integer(input.runAttempt)) fail('Invalid run identity.');
  const json = dependencies.json ?? publicJson;
  const provider = dependencies.loadRelease ?? loadRelease;
  const main = await json(`${api}/branches/main`);
  if (main?.name !== 'main' || main.protected !== true || main.commit?.sha !== input.sourceSha) fail('Source must be the current protected main SHA.');
  if (input.eventName === 'push' && input.eventSha !== input.sourceSha) fail('Superseded push cannot publish current main.');
  const definition = await json(`${api}/contents/${workflowPath}?ref=${input.workflowSha}`);
  const current = await json(`${api}/contents/${workflowPath}?ref=${input.sourceSha}`);
  for (const file of [definition, current]) {
    if (file?.type !== 'file' || file.path !== workflowPath || !sha.test(file.sha)) fail('Workflow definition is unavailable.');
  }
  if (definition.sha !== current.sha) fail('Event workflow definition differs from trusted main; retry from main.');
  const previous = await json(`${provenanceUrl}?run=${input.runId}-${input.runAttempt}`, input.policy === 'initial');
  if (previous === undefined && input.policy !== 'initial') fail('An established site must retain its deployed provenance.');
  if (previous !== undefined) {
    validateProvenance(previous);
    if (input.runNumber < previous.runNumber || (input.runNumber === previous.runNumber && input.runAttempt < previous.runAttempt)) fail('Superseded run cannot replace a newer deployment.');
    if (previous.sourceSha !== input.sourceSha) {
      const ancestry = await json(`${api}/compare/${previous.sourceSha}...${input.sourceSha}`);
      if (ancestry?.status !== 'ahead' || ancestry.merge_base_commit?.sha !== previous.sourceSha || ancestry.base_commit?.sha !== previous.sourceSha) fail('Main does not descend from the deployed source.');
    }
  }
  let result = await provider({ mode: 'initial-publication' });
  if (result.status !== 'ready') fail('Complete stable release required.');
  const minimumVersion = previous?.version ?? result.release.version;
  const expectedId = input.eventName === 'release' ? event.release.id : result.release.id;
  const expectedTag = input.eventName === 'release' ? event.release.tag_name : result.release.tag;
  if (input.eventName === 'workflow_dispatch' && event.inputs.release_tag !== expectedTag) fail('Manual tag is no longer the latest stable release.');
  result = await provider({ mode: 'release-published', expectedId, expectedTag, minimumVersion });
  if (result.status !== 'ready') fail('Complete current event release required.');
  if (compareVersions(result.release.version, minimumVersion) < 0) fail('Release version rollback refused.');
  return {
    schema: 1, sourceSha: input.sourceSha, workflowSha: input.workflowSha,
    releaseId: result.release.id, releaseTag: result.release.tag, version: result.release.version,
    releaseFingerprint: hash(JSON.stringify(result.release)), minimumVersion,
    runId: input.runId, runNumber: input.runNumber, runAttempt: input.runAttempt,
  };
}

export function assertSamePlan(expected, actual) {
  for (const key of ['schema', 'sourceSha', 'workflowSha', 'releaseId', 'releaseTag', 'version', 'releaseFingerprint', 'runId', 'runNumber', 'runAttempt']) {
    if (expected?.[key] !== actual[key]) fail(`Build/deployment evidence changed (${key}).`);
  }
}

export async function verifyDownloads(release, request = fetch) {
  for (const asset of release.packages) {
    const response = await request(asset.href, { method: 'HEAD', redirect: 'manual', cache: 'no-store', signal: AbortSignal.timeout(8000) });
    if (response.status === 200) continue;
    if (![301, 302, 303, 307, 308].includes(response.status)) fail('Official download is unavailable.');
    const location = new URL(response.headers.get('location') ?? '', asset.href);
    if (location.protocol !== 'https:' || location.username || location.password ||
        !['release-assets.githubusercontent.com', 'objects.githubusercontent.com'].includes(location.hostname)) fail('Download redirected outside official storage.');
  }
}

export async function artifactInventory(output) {
  const root = await lstat(output);
  if (!root.isDirectory() || root.isSymbolicLink()) fail('Pages output root must be a real directory.');
  const entries = [];
  async function visit(directory, prefix = '') {
    for (const name of (await readdir(directory)).sort()) {
      const path = resolve(directory, name);
      const relative = prefix + name;
      const info = await lstat(path);
      if (info.isSymbolicLink() || (!info.isDirectory() && !info.isFile()) || (info.isFile() && info.nlink !== 1)) fail('Links or special files are forbidden in Pages output.');
      if (name.startsWith('.') || ['node_modules', 'src', 'tests', 'scripts', 'artifacts', 'test-results'].includes(name) || /\.(map|ts|astro|mjs|log|yml|yaml|lock|pdb)$/i.test(name)) fail('Unexpected source/cache/log in Pages output.');
      if (info.isDirectory()) await visit(path, `${relative}/`);
      else if (relative !== 'build-provenance.json') {
        if (!['.html', '.xml', '.js', '.css', '.png', '.webp', '.svg', '.woff2', '.txt', '.ico'].includes(extname(name).toLowerCase())) fail('Unexpected file type in static Pages output.');
        const checksum = createHash('sha256');
        for await (const bytes of createReadStream(path)) checksum.update(bytes);
        entries.push({ path: relative, size: info.size, sha256: checksum.digest('hex') });
      }
    }
  }
  await visit(output);
  if (!entries.some(item => item.path === 'index.html') || !entries.some(item => item.path === 'sitemap.xml')) fail('Missing static site entry points.');
  return { files: entries.length, bytes: entries.reduce((total, item) => total + item.size, 0), filesDigest: hash(JSON.stringify(entries)) };
}

function runtimeInput(environment) {
  return {
    eventName: environment.GITHUB_EVENT_NAME, repository: environment.GITHUB_REPOSITORY,
    ref: environment.GITHUB_REF, eventSha: environment.GITHUB_SHA, policy: environment.WEBSITE_DEPLOY_POLICY ?? '',
    sourceSha: environment.SITE_SOURCE_SHA, workflowSha: environment.SITE_WORKFLOW_SHA,
    workflowRef: environment.SITE_WORKFLOW_REF, runId: Number(environment.GITHUB_RUN_ID), runNumber: Number(environment.GITHUB_RUN_NUMBER), runAttempt: Number(environment.GITHUB_RUN_ATTEMPT),
  };
}

async function command(name) {
  const environment = process.env;
  const input = runtimeInput(environment);
  const event = JSON.parse(await readFile(environment.GITHUB_EVENT_PATH, 'utf8'));
  if (name === 'eligibility') {
    await appendFile(environment.GITHUB_OUTPUT, `publish=${publicationAllowed(input, event)}\n`);
    return;
  }
  if (!['prepare', 'record', 'recheck'].includes(name)) fail('Unknown guard command.');
  for (const key of ['NODE_OPTIONS', 'SITE_TEST_FIXTURE', 'SITE_TEST_SCENARIO']) if (environment[key] !== undefined) fail('Injected build fixtures/options are forbidden.');
  if (execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim() !== input.sourceSha) fail('Checkout SHA differs from expected source.');
  const plan = await preparePublication(input, event);
  if (name === 'prepare') {
    await appendFile(environment.GITHUB_OUTPUT, `plan=${JSON.stringify(plan)}\nrelease_id=${plan.releaseId}\nrelease_tag=${plan.releaseTag}\nminimum_version=${plan.minimumVersion}\n`);
  } else {
    assertSamePlan(JSON.parse(environment.SITE_PLAN), plan);
    if (name === 'record') {
      const output = resolve('dist');
      await validateOutput(output, 'release-candidate');
      const result = await loadRelease({ mode: 'release-published', expectedId: plan.releaseId, expectedTag: plan.releaseTag, minimumVersion: plan.minimumVersion });
      if (result.status !== 'ready' || hash(JSON.stringify(result.release)) !== plan.releaseFingerprint) fail('Release changed after the build.');
      const html = await readFile(resolve(output, 'index.html'), 'utf8');
      if (!html.includes(`"softwareVersion":"${plan.version}"`) || result.release.packages.some(asset => !html.includes(asset.href))) fail('Built downloads differ from the approved release.');
      await verifyDownloads(result.release);
      const inventory = await artifactInventory(output);
      const provenance = { ...plan, filesDigest: inventory.filesDigest };
      validateProvenance(provenance);
      await writeFile(resolve(output, 'build-provenance.json'), JSON.stringify(provenance, null, 2) + '\n');
      console.info('[publication-guard] verified artifact', JSON.stringify({ ...inventory, sourceSha: plan.sourceSha, releaseTag: plan.releaseTag }));
    }
  }
  console.info('[publication-guard] PASS', name, plan.sourceSha, plan.releaseTag);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { await command(process.argv[2]); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
