import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import test from 'node:test';
import { createReleaseLoader, loadRelease, ReleaseDataError, validateReleaseMetadata, validateChecksums } from '../src/lib/releases.ts';
import { api, web, completeRelease, fakeService } from './fixtures/releases.mjs';

const latest = `${api}/releases/latest`;
const ordinary = { mode: 'ordinary' };
const event = { mode: 'release-published', expectedId: 84, expectedTag: 'v2.4.7', minimumVersion: '2.4.6' };
const invalidData = error => error instanceof ReleaseDataError && error.code === 'invalid-data';
const incomplete = error => error instanceof ReleaseDataError && error.code === 'incomplete-release';
const response = (status, headers = {}) => new Response('', { status, headers });

test('complete stable release maps five packages in platform order without binary requests', async () => {
  const fixture = completeRelease(), service = fakeService(fixture);
  const result = await loadRelease(ordinary, service.dependencies);
  assert.equal(result.status, 'ready');
  assert.equal(result.release.version, '2.4.7');
  assert.deepEqual(result.release.packages.map(item => item.kind), ['windows-installer', 'windows-portable', 'linux-deb', 'linux-rpm', 'linux-portable']);
  assert.deepEqual(result.release.packages.map(item => item.platform), ['windows', 'windows', 'linux', 'linux', 'linux']);
  assert.deepEqual(result.release.packages.map(item => item.href), fixture.payload.assets.slice(0, 5).map(item => item.browser_download_url));
  assert.equal(result.release.requirementsUrl, fixture.requirements.html_url);
  assert.deepEqual(result.release.integrity, { metadataValidated: true, checksumFileValidated: true, checksumFileDigestVerified: true, binaryBytesVerified: false });
  assert.equal(service.calls.length, 3);
  for (const call of service.calls) {
    assert.ok(!/\.(?:exe|zip|deb|rpm|gz)$/.test(call.url));
    assert.equal(call.options.redirect, 'manual');
    assert.ok(call.options.signal instanceof AbortSignal);
    assert.ok(!Object.keys(call.options.headers).some(name => name.toLowerCase() === 'authorization'));
  }
  assert.deepEqual(service.warnings, []);
});

test('later arbitrary stable versions need no manual package-list edit; missing API digests are allowed', async () => {
  const fixture = completeRelease('3.0.12'); fixture.payload.assets.forEach(item => { delete item.digest; });
  const result = await loadRelease(ordinary, fakeService(fixture).dependencies);
  assert.equal(result.release.version, '3.0.12');
  assert.ok(result.release.packages.every(item => item.sha256.length === 64 && item.apiSha256 === null));
  assert.equal(result.release.integrity.binaryBytesVerified, false);
});

const mutations = {
  'unexpected version': f => { f.payload.tag_name = 'v9.9.9'; },
  prerelease: f => { f.payload.prerelease = true; },
  draft: f => { f.payload.draft = true; },
  'unpublished release': f => { f.payload.published_at = null; },
  'invalid calendar date': f => { f.payload.published_at = '2026-02-30T11:46:21Z'; },
  'wrong repository': f => { f.payload.url = f.payload.url.replace('melnorme6', 'example'); },
  'release page tag mismatch': f => { f.payload.html_url = `${web}/releases/tag/v2.4.6`; },
  'non-GitHub asset': f => { f.payload.assets[0].browser_download_url = 'https://example.invalid/setup.exe'; },
  'wrong asset version': f => { f.payload.assets[0].name = 'ExtSieve-Setup-2.4.6-win-x64.exe'; },
  'source archive masquerading as package': f => { f.payload.assets[0].browser_download_url = `${web}/archive/refs/tags/v2.4.7.zip`; },
  'duplicate asset': f => { f.payload.assets.push(structuredClone(f.payload.assets[0])); },
  'duplicate ID': f => { f.payload.assets[1].id = f.payload.assets[0].id; },
  'zero size': f => { f.payload.assets[0].size = 0; },
  'negative size': f => { f.payload.assets[0].size = -1; },
  'fractional size': f => { f.payload.assets[0].size = 1.5; },
  'oversized checksum': f => { f.payload.assets[5].size = 8193; },
  'invalid digest': f => { f.payload.assets[0].digest = 'md5:1234'; },
  'asset query': f => { f.payload.assets[0].browser_download_url += '?download=1'; },
  'asset fragment': f => { f.payload.assets[0].browser_download_url += '#file'; },
  'URL credentials': f => { f.payload.assets[0].browser_download_url = f.payload.assets[0].browser_download_url.replace('https://', 'https://user:password@'); },
  'URL non-HTTPS': f => { f.payload.assets[0].browser_download_url = f.payload.assets[0].browser_download_url.replace('https:', 'http:'); },
  'URL normalization traversal': f => { f.payload.assets[0].browser_download_url = f.payload.assets[0].browser_download_url.replace('/download/', '/other/../download/'); },
  'wrong asset API repository': f => { f.payload.assets[0].url = f.payload.assets[0].url.replace('ExtSieve', 'Another'); },
  'extra unsupported package': f => { const asset = structuredClone(f.payload.assets[0]); asset.id = 1234; asset.name = 'source.zip'; f.payload.assets.push(asset); },
};
for (const [name, mutate] of Object.entries(mutations)) test(`contradictory metadata fails ordinary builds: ${name}`, async () => {
  const fixture = completeRelease(); mutate(fixture);
  const service = fakeService(fixture);
  await assert.rejects(loadRelease(ordinary, service.dependencies), invalidData);
  assert.deepEqual(service.warnings, []); assert.equal(service.calls.length, 1);
});

for (const version of ['v2.4.7-beta.1', 'v2.4.7+build', '2.4.7', 'v02.4.7', 'v2.4', 'v9007199254740992.0.0']) {
  test(`reject unstable or noncanonical tag ${version}`, () => {
    const fixture = completeRelease(); fixture.payload.tag_name = version;
    assert.throws(() => validateReleaseMetadata(fixture.payload), invalidData);
  });
}

for (const mode of [ordinary, event, { mode: 'initial-publication' }]) {
  for (const kind of ['package', 'checksum', 'upload']) test(`${mode.mode} rejects incomplete ${kind}`, async () => {
    const fixture = completeRelease();
    if (kind === 'package') fixture.payload.assets.splice(0, 1);
    if (kind === 'checksum') fixture.payload.assets.pop();
    if (kind === 'upload') fixture.payload.assets[0].state = 'new';
    const service = fakeService(fixture);
    await assert.rejects(loadRelease(mode, service.dependencies), incomplete);
    assert.deepEqual(service.warnings, []);
  });
}

test('a stable-release absence is distinct from API failure; repository and listing are confirmed', async () => {
  for (const listed of [[], [{ draft: false, prerelease: true }], [{ draft: true, prerelease: false }]]) {
    const service = fakeService(completeRelease(), {
      [latest]: () => response(404), [api]: { full_name: 'melnorme6/ExtSieve', private: false }, [`${api}/releases?per_page=100`]: listed,
    });
    const result = await loadRelease(ordinary, service.dependencies);
    assert.deepEqual(result, { status: 'no-stable-release', releasesUrl: `${web}/releases` });
    assert.equal(service.calls.length, 3); assert.deepEqual(service.warnings, []);
  }
});

test('404 never masks a missing repository or a contradictory listing', async () => {
  for (const repo of [{ full_name: 'example/Another', private: false }, { full_name: 'melnorme6/ExtSieve', private: true }]) {
    await assert.rejects(loadRelease(ordinary, fakeService(completeRelease(), { [latest]: () => response(404), [api]: repo }).dependencies), invalidData);
  }
  await assert.rejects(loadRelease(ordinary, fakeService(completeRelease(), {
    [latest]: () => response(404), [api]: { full_name: 'melnorme6/ExtSieve', private: false }, [`${api}/releases?per_page=100`]: [{ draft: false, prerelease: false }],
  }).dependencies), invalidData);
});

for (const [name, fail] of Object.entries({
  network: () => { throw new TypeError('Synthetic network outage'); },
  timeout: () => { throw new DOMException('Synthetic timeout', 'TimeoutError'); },
  transient: () => response(503), rateLimit: () => response(429),
  primaryRateLimit: () => response(403, { 'x-ratelimit-remaining': '0' }),
  secondaryRateLimit: () => response(403, { 'retry-after': '60' }),
})) test(`bounded retry and ordinary fallback: ${name}`, async () => {
  const service = fakeService(completeRelease(), { [latest]: fail });
  const result = await loadRelease(ordinary, service.dependencies);
  assert.deepEqual(result, { status: 'temporarily-unavailable', releasesUrl: `${web}/releases` });
  const rateLimited = name.toLowerCase().includes('ratelimit');
  assert.equal(service.calls.length, rateLimited ? 1 : 2);
  assert.deepEqual(service.waits, rateLimited ? [] : [250]); assert.equal(service.warnings.length, 1);
});

test('an actual abort bounds stalled requests', async () => {
  const service = fakeService(completeRelease(), { [latest]: async (_url, { signal }) => new Promise((_resolve, reject) => {
    signal.addEventListener('abort', () => reject(signal.reason), { once: true });
    // Keep the event loop alive until AbortSignal.timeout fires.
    const timer = setTimeout(() => reject(new Error('Timeout not honored')), 250);
    signal.addEventListener('abort', () => clearTimeout(timer), { once: true });
  }) });
  const result = await loadRelease(ordinary, { ...service.dependencies, timeoutMs: 10 });
  assert.equal(result.status, 'temporarily-unavailable'); assert.equal(service.calls.length, 2);
});

test('retry can recover a transient response', async () => {
  const fixture = completeRelease(); let attempts = 0;
  const service = fakeService(fixture, { [latest]: () => ++attempts === 1 ? response(502) : new Response(JSON.stringify(fixture.payload), { headers: { 'content-type': 'application/json' } }) });
  assert.equal((await loadRelease(ordinary, service.dependencies)).status, 'ready');
  assert.equal(attempts, 2); assert.deepEqual(service.warnings, []);
});

for (const mode of [event, { mode: 'initial-publication' }]) test(`${mode.mode} never permits transient fallback or absence`, async () => {
  await assert.rejects(loadRelease(mode, fakeService(completeRelease(), { [latest]: () => response(503) }).dependencies), error => error.code === 'release-identity');
  await assert.rejects(loadRelease(mode, fakeService(completeRelease(), { [latest]: () => response(404), [api]: { full_name: 'melnorme6/ExtSieve', private: false }, [`${api}/releases?per_page=100`]: [] }).dependencies), error => error.code === 'release-identity');
});

for (const payload of ['{invalid', 'null', '[]', '{"assets":[]}']) test(`malformed JSON or shape fails without fallback: ${payload}`, async () => {
  const service = fakeService(completeRelease(), { [latest]: () => new Response(payload, { headers: { 'content-type': 'application/json' } }) });
  await assert.rejects(loadRelease(ordinary, service.dependencies), invalidData); assert.deepEqual(service.warnings, []);
});

test('unexpected permission failures, redirects, content types and oversized responses fail closed', async () => {
  for (const fail of [() => response(403), () => response(401), () => response(302, { location: `${api}/releases/84` }),
    () => new Response('{}', { headers: { 'content-type': 'text/html' } }),
    () => new Response('{}', { headers: { 'content-type': 'application/json', 'content-length': '1048577' } }),
    () => new Response(' '.repeat(1048577), { headers: { 'content-type': 'application/json' } })]) {
    await assert.rejects(loadRelease(ordinary, fakeService(completeRelease(), { [latest]: fail }).dependencies), invalidData);
  }
});

for (const kind of ['missing entry', 'wrong filename', 'duplicate entry', 'wrong hash', 'bad separator', 'bad checksum digest', 'wrong size']) {
  test(`SHA256SUMS is validated: ${kind}`, async () => {
    const fixture = completeRelease(); let content = fixture.checksumText;
    if (kind === 'missing entry') content = content.split('\n').slice(1).join('\n');
    if (kind === 'wrong filename') content = content.replace('win-x64.exe', 'win-x86.exe');
    if (kind === 'duplicate entry') content += content.split('\n')[0] + '\n';
    if (kind === 'wrong hash') content = '0'.repeat(64) + content.slice(64);
    if (kind === 'bad separator') content = content.replace('  ', ' ');
    fixture.payload.assets[5].size = Buffer.byteLength(content);
    fixture.payload.assets[5].digest = `sha256:${createHash('sha256').update(content).digest('hex')}`;
    if (kind === 'bad checksum digest') fixture.payload.assets[5].digest = `sha256:${'0'.repeat(64)}`;
    if (kind === 'wrong size') fixture.payload.assets[5].size++;
    await assert.rejects(validateChecksums(Buffer.from(content), validateReleaseMetadata(fixture.payload)), invalidData);
  });
}

test('Linux requirements must be a real file at the verified release tag', async () => {
  const fixture = completeRelease(); fixture.requirements.html_url = `${web}/blob/main/LINUX_REQUIREMENTS.md`;
  await assert.rejects(loadRelease(ordinary, fakeService(fixture).dependencies), invalidData);
});

test('checksum delivery permits official signed GitHub storage but rejects external redirect hosts', async () => {
  const fixture = completeRelease(), checksumUrl = fixture.payload.assets[5].browser_download_url;
  const delivery = 'https://release-assets.githubusercontent.com/github-production-release-asset/example?signature=synthetic';
  const service = fakeService(fixture, { [checksumUrl]: () => response(302, { location: delivery }), [delivery]: () => new Response(fixture.checksumText) });
  assert.equal((await loadRelease(ordinary, service.dependencies)).status, 'ready');
  for (const location of ['https://example.invalid/file', 'http://objects.githubusercontent.com/file', 'https://user:password@objects.githubusercontent.com/file', 'http://[']) {
    await assert.rejects(loadRelease(ordinary, fakeService(fixture, { [checksumUrl]: () => response(302, { location }) }).dependencies), invalidData);
  }
});

test('a checksum transfer outage falls back ordinarily but fails publication', async () => {
  const fixture = completeRelease(), overrides = { [fixture.payload.assets[5].browser_download_url]: () => response(503) };
  assert.equal((await loadRelease(ordinary, fakeService(fixture, overrides).dependencies)).status, 'temporarily-unavailable');
  await assert.rejects(loadRelease(event, fakeService(fixture, overrides).dependencies), error => error.code === 'release-identity');
});

test('release events fetch exact identity and must match the current latest release', async () => {
  const service = fakeService(); assert.equal((await loadRelease(event, service.dependencies)).status, 'ready');
  assert.ok(service.calls.some(call => call.url === `${api}/releases/84`));
  await assert.rejects(loadRelease({ ...event, expectedTag: 'v2.4.8' }, fakeService().dependencies), error => error.code === 'release-identity');
  const another = completeRelease('2.4.7', 85);
  await assert.rejects(loadRelease(event, fakeService(another, { [`${api}/releases/84`]: completeRelease().payload }).dependencies), error => error.code === 'release-identity');
});

test('obsolete release events and deployment version rollback are rejected numerically', async () => {
  const old = completeRelease('2.4.7', 84), current = completeRelease('2.10.0', 85);
  await assert.rejects(loadRelease(event, fakeService(current, { [`${api}/releases/84`]: old.payload }).dependencies), error => error.code === 'stale-release');
  await assert.rejects(loadRelease({ ...event, minimumVersion: '2.10.0' }, fakeService().dependencies), error => error.code === 'stale-release');
});

test('initial publication succeeds only with a complete validated stable release', async () => {
  assert.equal((await loadRelease({ mode: 'initial-publication' }, fakeService().dependencies)).status, 'ready');
});

test('one shared promise supplies all consumers without duplicate API requests', async () => {
  const service = fakeService(), loader = createReleaseLoader(ordinary, service.dependencies);
  const first = loader(), second = loader(); assert.equal(first, second);
  const [a, b] = await Promise.all([first, second]); assert.equal(a, b); assert.equal(service.calls.length, 3);
});
