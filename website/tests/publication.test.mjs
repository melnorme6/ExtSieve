import assert from 'node:assert/strict';
import test from 'node:test';
import { createHash } from 'node:crypto';
import { readFile, mkdtemp, mkdir, writeFile, readdir, stat, rm, link, symlink } from 'node:fs/promises';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseDocument } from 'yaml';
import { publicationAllowed, preparePublication, assertSamePlan, artifactInventory, publicJson, verifyDownloads, validateProvenance } from '../scripts/ci-publication.mjs';
import { readBuildContext, loadRelease } from '../src/lib/releases.ts';
import { completeRelease, fakeService, api } from './fixtures/releases.mjs';

const workflow = parseDocument(await readFile(new URL('../../.github/workflows/website.yml', import.meta.url), 'utf8'), { uniqueKeys: true });
assert.deepEqual(workflow.errors, []);
const config = workflow.toJS();
const dependabot = parseDocument(await readFile(new URL('../../.github/dependabot.yml', import.meta.url), 'utf8'), { uniqueKeys: true });
assert.deepEqual(dependabot.errors, []);
const sourceSha = 'a'.repeat(40), workflowSha = 'b'.repeat(40), blobSha = 'c'.repeat(40);
const event = { repository: { full_name: 'melnorme6/ExtSieve', private: false, default_branch: 'main' },
  action: 'published', release: { id: 84, tag_name: 'v2.4.7', draft: false, prerelease: false }, inputs: { release_tag: 'v2.4.7' } };
const input = { eventName: 'release', repository: 'melnorme6/ExtSieve', ref: 'refs/tags/v2.4.7', policy: 'initial',
  sourceSha, workflowSha, workflowRef: 'melnorme6/ExtSieve/.github/workflows/website.yml@refs/tags/v2.4.7', runId: 200, runNumber: 200, runAttempt: 1 };
const fingerprint = release => createHash('sha256').update(JSON.stringify(release)).digest('hex');
function dependencies(fixture = completeRelease(), overrides = {}, metadata = {}) {
  const service = fakeService(fixture, overrides);
  return {
    loadRelease: context => loadRelease(context, service.dependencies),
    json: async url => {
      if (Object.hasOwn(metadata, url)) return typeof metadata[url] === 'function' ? metadata[url]() : metadata[url];
      if (url === `${api}/branches/main`) return { name: 'main', protected: true, commit: { sha: sourceSha } };
      if (url.includes('/contents/.github/workflows/website.yml?ref=')) return { type: 'file', path: '.github/workflows/website.yml', sha: blobSha };
      if (url.startsWith('https://melnorme6.github.io/ExtSieve/build-provenance.json?')) return undefined;
      throw new Error(`Unexpected public fixture URL: ${url}`);
    },
  };
}
const prepare = (change = {}, changedEvent = event, deps = dependencies()) => preparePublication({ ...input, ...change }, changedEvent, deps);

for (const actor of ['untrusted-contributor', 'dependabot[bot]']) {
  test(`${actor} pull request has read-only checks and cannot publish`, async () => {
    assert.equal(publicationAllowed({ ...input, eventName: 'pull_request', actor }, event), false);
    await assert.rejects(prepare({ eventName: 'pull_request', actor }), /disabled/);
    assert.deepEqual(config.permissions, { contents: 'read' });
    assert.deepEqual(config.jobs.verify.permissions, { contents: 'read' });
    assert.match(config.jobs.deploy.if, /event_name != 'pull_request'/);
    assert.deepEqual(config.jobs.build.permissions, { contents: 'read' });
    assert.match(config.jobs.build.if, /event_name == 'pull_request'/);
    for (const step of config.jobs.build.steps.filter(step => step.uses?.startsWith('actions/upload-pages-artifact@') || step.id === 'plan')) assert.equal(step.if, "github.event_name != 'pull_request'");
    assert.equal(config.jobs.verify.environment, undefined);
    assert.equal(config.on.pull_request_target, undefined);
  });
}
test('real path filters exclude desktop-only pushes and include website/workflow/maintenance changes', () => {
  const matches = path => config.on.push.paths.some(pattern => pattern.endsWith('/**') ? path.startsWith(pattern.slice(0, -2)) : path === pattern);
  assert.equal(matches('src/ExtSieve.Core/Archive.cs'), false);
  for (const path of ['website/src/pages/index.astro', 'website/package-lock.json', 'website/.npmrc', '.github/workflows/website.yml', '.github/dependabot.yml']) assert.equal(matches(path), true);
  assert.deepEqual(config.on.push.branches, ['main']);
  assert.deepEqual(config.on.pull_request.paths, config.on.push.paths);
  assert.deepEqual(config.on.release.types, ['published']);
  assert.equal(config.on.workflow_dispatch.inputs.release_tag.required, true);
});
for (const flag of ['draft', 'prerelease']) {
  test(`${flag} publication is refused before API access`, async () => {
    const changedEvent = structuredClone(event); changedEvent.release[flag] = true;
    assert.equal(publicationAllowed(input, changedEvent), false);
    await assert.rejects(prepare({}, changedEvent), /disabled/);
  });
}
test('a complete stable release uses current main rather than tag source and binds its full inventory', async () => {
  const plan = await prepare();
  const result = await loadRelease({ mode: 'initial-publication' }, fakeService().dependencies);
  assert.equal(plan.sourceSha, sourceSha);
  assert.notEqual(plan.sourceSha, workflowSha);
  assert.equal(plan.releaseTag, 'v2.4.7');
  assert.equal(plan.releaseFingerprint, fingerprint(result.release));
});
test('a stable incomplete release cannot publish', async () => {
  const fixture = completeRelease(); fixture.payload.assets.pop();
  await assert.rejects(prepare({}, event, dependencies(fixture)), /Five application packages and SHA256SUMS are required/);
});
test('obsolete release identity and tag events cannot publish latest data', async () => {
  const changedEvent = structuredClone(event); changedEvent.release.tag_name = 'v2.4.6';
  await assert.rejects(prepare({}, changedEvent), /expected event/);
});
test('API outage is fail-closed for publication while ordinary preview retains its approved fallback', async () => {
  const overrides = { [`${api}/releases/latest`]: () => new Response('', { status: 503 }) };
  await assert.rejects(prepare({}, event, dependencies(completeRelease(), overrides)), /fallback is forbidden/);
  assert.equal((await loadRelease({ mode: 'ordinary' }, fakeService(completeRelease(), overrides).dependencies)).status, 'temporarily-unavailable');
  await assert.rejects(prepare({}, event, dependencies(completeRelease(), {}, { [`${api}/branches/main`]: () => { throw new Error('API outage'); } })), /API outage/);
});
test('contradictory asset metadata cannot become publication evidence', async () => {
  const fixture = completeRelease(); fixture.payload.assets[0].browser_download_url += '?unexpected=1';
  await assert.rejects(prepare({}, event, dependencies(fixture)), /official repository/);
});
test('first go-live requires explicit policy, public repository and protected main', async () => {
  for (const policy of ['', undefined, 'disabled']) assert.equal(publicationAllowed({ ...input, policy }, event), false);
  await assert.rejects(prepare({ policy: 'disabled' }), /disabled/);
  await assert.rejects(prepare({}, event, dependencies(completeRelease(), {}, { [`${api}/branches/main`]: { name: 'main', protected: false, commit: { sha: sourceSha } } })), /protected main/);
  assert.equal(publicationAllowed({ ...input, repository: 'example/ExtSieve' }, event), false);
  assert.throws(() => publicationAllowed({ ...input, policy: 'true' }, event), /Unknown/);
});
test('a changed event workflow definition, stale source or stale push fails provenance', async () => {
  await assert.rejects(prepare({ sourceSha: 'd'.repeat(40) }), /current protected main/);
  const url = `${api}/contents/.github/workflows/website.yml?ref=${workflowSha}`;
  await assert.rejects(prepare({}, event, dependencies(completeRelease(), {}, { [url]: { type: 'file', path: '.github/workflows/website.yml', sha: 'd'.repeat(40) } })), /definition differs/);
  await assert.rejects(prepare({ eventName: 'push', ref: 'refs/heads/main', eventSha: workflowSha }), /Superseded push/);
  const plan = await prepare({ eventName: 'push', ref: 'refs/heads/main', eventSha: sourceSha });
  assert.equal(plan.sourceSha, sourceSha);
});
test('manual retries require main and exactly the current stable tag', async () => {
  await assert.rejects(prepare({ eventName: 'workflow_dispatch' }), /Only main/);
  const changedEvent = structuredClone(event); changedEvent.inputs.release_tag = 'v2.4.6';
  await assert.rejects(prepare({ eventName: 'workflow_dispatch', ref: 'refs/heads/main' }, changedEvent), /no longer/);
  assert.equal((await prepare({ eventName: 'workflow_dispatch', ref: 'refs/heads/main' })).releaseTag, 'v2.4.7');
});
test('established deployments require provenance and reject rollback and older concurrent runs', async () => {
  const plan = await prepare();
  const previous = { ...plan, filesDigest: 'e'.repeat(64), runId: 199, runNumber: 199 };
  const url = 'https://melnorme6.github.io/ExtSieve/build-provenance.json?run=200-1';
  const deps = record => dependencies(completeRelease(), {}, { [url]: record });
  await assert.rejects(prepare({ policy: 'enabled' }, event, deps(undefined)), /retain/);
  await assert.rejects(prepare({ policy: 'initial' }, event, deps(null)), /Invalid deployed/);
  assertSamePlan(plan, await prepare({ policy: 'enabled' }, event, deps(previous)));
  await assert.rejects(prepare({ policy: 'enabled' }, event, deps({ ...previous, runId: 201, runNumber: 201 })), /Superseded run/);
  await assert.rejects(prepare({ policy: 'enabled' }, event, deps({ ...previous, runId: 200, runNumber: 200, runAttempt: 2 })), /Superseded run/);
  await assert.rejects(prepare({ policy: 'enabled' }, event, deps({ ...previous, version: '2.10.0', releaseTag: 'v2.10.0' })), /older release/);
  await assert.rejects(prepare({ policy: 'enabled' }, event, deps({ ...previous, filesDigest: 'invalid' })), /Invalid deployed/);
});
test('new source must descend from deployed public source', async () => {
  const plan = await prepare(), oldSha = 'd'.repeat(40);
  const previous = { ...plan, sourceSha: oldSha, filesDigest: 'e'.repeat(64), runId: 199, runNumber: 199 };
  const base = { ['https://melnorme6.github.io/ExtSieve/build-provenance.json?run=200-1']: previous };
  const url = `${api}/compare/${oldSha}...${sourceSha}`;
  await assert.rejects(prepare({ policy: 'enabled' }, event, dependencies(completeRelease(), {}, { ...base, [url]: { status: 'diverged' } })), /descend/);
  assertSamePlan(plan, await prepare({ policy: 'enabled' }, event, dependencies(completeRelease(), {}, { ...base, [url]: { status: 'ahead', base_commit: { sha: oldSha }, merge_base_commit: { sha: oldSha } } })));
});
test('immediate pre-deploy checks reject changed release bytes, identity, source and run', async () => {
  const plan = await prepare();
  for (const [key, value] of Object.entries({ releaseFingerprint: 'e'.repeat(64), releaseId: 85, releaseTag: 'v2.5.0', sourceSha: workflowSha, runId: 201 })) assert.throws(() => assertSamePlan({ ...plan, [key]: value }, plan), /evidence changed/);
  const fixture = completeRelease(); fixture.payload.assets[0].size++;
  assert.throws(() => assertSamePlan(plan, { ...plan, releaseFingerprint: 'f'.repeat(64) }), /evidence changed/);
  const updated = await prepare({}, event, dependencies(fixture));
  assert.throws(() => assertSamePlan(plan, updated), /releaseFingerprint/);
});
test('candidate build context binds event identity without changing private preview defaults', () => {
  assert.deepEqual(readBuildContext({}), { mode: 'ordinary' });
  assert.deepEqual(readBuildContext({ SITE_BUILD_MODE: 'release-candidate' }), { mode: 'initial-publication' });
  const env = { SITE_BUILD_MODE: 'release-candidate', SITE_EXPECTED_RELEASE_ID: '84', SITE_EXPECTED_RELEASE_TAG: 'v2.4.7', SITE_MINIMUM_VERSION: '2.4.6' };
  assert.deepEqual(readBuildContext(env), { mode: 'release-published', expectedId: 84, expectedTag: 'v2.4.7', minimumVersion: '2.4.6' });
  for (const change of [{ SITE_BUILD_MODE: 'private-preview' }, { SITE_EXPECTED_RELEASE_ID: '084' }, { SITE_EXPECTED_RELEASE_TAG: 'v2.4.7-beta' }, { SITE_MINIMUM_VERSION: undefined }, { SITE_TEST_FIXTURE: 'synthetic-release' }]) assert.throws(() => readBuildContext({ ...env, ...change }));
});
test('download checks use only HEAD and reject unavailable or off-site redirects', async () => {
  const { release } = await loadRelease({ mode: 'initial-publication' }, fakeService().dependencies);
  let calls = 0;
  await verifyDownloads(release, async (url, options) => { assert.ok(release.packages.some(asset => asset.href === url)); assert.equal(options.method, 'HEAD'); assert.equal(options.redirect, 'manual'); calls++; return new Response(null, { status: 302, headers: { location: 'https://release-assets.githubusercontent.com/synthetic' } }); });
  assert.equal(calls, 5);
  await assert.rejects(verifyDownloads(release, async () => new Response(null, { status: 404 })), /unavailable/);
  await assert.rejects(verifyDownloads(release, async () => new Response(null, { status: 302, headers: { location: 'https://example.invalid/package' } })), /outside/);
});
test('bounded public metadata rejects outage, invalid UTF-8/JSON and oversized bodies', async () => {
  assert.equal(await publicJson('https://example.invalid', true, async () => new Response(null, { status: 404 })), undefined);
  assert.equal(await publicJson('https://example.invalid', true, async () => new Response('null')), null);
  await assert.rejects(publicJson('https://example.invalid', false, async () => new Response(null, { status: 404 })), /failed/);
  for (const body of ['{invalid', new Uint8Array([0xff]), 'x'.repeat(1024 * 1024 + 1)]) await assert.rejects(publicJson('https://example.invalid', false, async () => new Response(body)));
});
test('exact artifact inventory is deterministic and rejects sources and hard links', async () => {
  const parent = fileURLToPath(new URL('../test-results/publication/', import.meta.url));
  await mkdir(parent, { recursive: true });
  const scratch = await mkdtemp(resolve(parent, 'inventory-'));
  await writeFile(resolve(scratch, 'index.html'), '<html>synthetic</html>');
  await writeFile(resolve(scratch, 'sitemap.xml'), '<urlset/>');
  const first = await artifactInventory(scratch);
  assert.deepEqual(await artifactInventory(scratch), first);
  await writeFile(resolve(scratch, 'build-provenance.json'), '{}');
  assert.deepEqual(await artifactInventory(scratch), first);
  await writeFile(resolve(scratch, 'unexpected.log'), 'synthetic');
  await assert.rejects(artifactInventory(scratch), /source\/cache\/log/);
  await rm(resolve(scratch, 'unexpected.log'));
  await writeFile(resolve(scratch, 'package.json'), '{}');
  await assert.rejects(artifactInventory(scratch), /Unexpected file type/);
  await rm(resolve(scratch, 'package.json'));
  const alias = scratch + '-root-link';
  await symlink(scratch, alias, 'junction');
  await assert.rejects(artifactInventory(alias), /real directory/);
  await rm(alias);
  await link(resolve(scratch, 'index.html'), resolve(scratch, 'alias.html'));
  await assert.rejects(artifactInventory(scratch), /Links/);
  assert.ok(scratch.startsWith(resolve(parent)));
  let bytes = 0, files = 0;
  for (const name of await readdir(scratch)) { bytes += (await stat(resolve(scratch, name))).size; files++; }
  await rm(scratch, { recursive: true });
  await writeFile(resolve(parent, 'cleanup.json'), JSON.stringify({ classification: 'task-owned synthetic artifact inventory', files, bytes, removed: true }) + '\n');
});
test('jobs enforce minimal grants, exact source/artifact dependencies and protected serial deployment', () => {
  assert.deepEqual(config.jobs.build.permissions, { contents: 'read' });
  assert.deepEqual(config.jobs.deploy.permissions, { contents: 'read', pages: 'write', 'id-token': 'write' });
  assert.deepEqual(config.jobs.deploy.needs, ['verify', 'build']);
  assert.equal(config.jobs.deploy.environment.name, 'github-pages');
  assert.equal(config.jobs.deploy.concurrency['cancel-in-progress'], false);
  assert.equal(config.jobs.deploy.concurrency.group, 'website-pages-deploy');
  assert.equal(config.concurrency['cancel-in-progress'], '${{ github.event_name == \'pull_request\' }}');
  const steps = Object.values(config.jobs).flatMap(job => job.steps);
  for (const step of steps.filter(step => step.uses)) {
    assert.match(step.uses, /^actions\/[a-z-]+@[a-f0-9]{40}$/);
    if (step.uses.startsWith('actions/checkout@')) assert.equal(step.with['persist-credentials'], false);
    if (step.uses.startsWith('actions/setup-node@')) { assert.equal(step.with['node-version'], '24.18.0'); assert.equal(step.with['package-manager-cache'], false); }
  }
  assert.equal(config.jobs.build.steps.find(step => step.uses?.startsWith('actions/upload-pages-artifact@')).with.path, 'website/dist');
  assert.ok(config.jobs.build.steps.some(step => step.run === 'npm run build:candidate'));
  assert.ok(config.jobs.build.steps.some(step => step.run === 'npm run test:browser'));
  const privilegedRuns = config.jobs.deploy.steps.filter(step => step.run).map(step => step.run);
  assert.deepEqual(privilegedRuns, ['node scripts/ci-publication.mjs recheck']);
  assert.deepEqual(config.jobs.deploy.steps.at(-1).with, { artifact_name: 'github-pages' });
});
test('weekly Dependabot paths and compatible groups match export and never auto-merge', () => {
  const updates = dependabot.toJS().updates;
  assert.deepEqual(updates.map(item => [item['package-ecosystem'], item.directory, item.schedule.interval]), [['npm', '/website', 'weekly'], ['github-actions', '/', 'weekly']]);
  assert.deepEqual(updates.map(item => item['open-pull-requests-limit']), [5, 3]);
  for (const update of updates) for (const group of Object.values(update.groups)) assert.deepEqual(group['update-types'], ['minor', 'patch']);
  assert.equal(config.on.pull_request.paths.includes('.github/dependabot.yml'), true);
  assert.equal(config.jobs.verify.steps.some(step => /auto.?merge/.test(step.run ?? '')), false);
  assert.throws(() => validateProvenance({}), /Invalid/);
});
