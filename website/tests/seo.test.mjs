import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, readFile, cp, writeFile, readdir, rm, mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { readSiteSettings, canonicalUrl } from '../src/lib/site-settings.ts';
import { softwareApplication, serializeJsonLd } from '../src/lib/seo.ts';
import { loadRelease } from '../src/lib/releases.ts';
import { completeRelease, fakeService } from './fixtures/releases.mjs';
import { validateOutput } from '../scripts/quality-gate.mjs';
import { orderCsp } from '../scripts/csp-order.mjs';

const output = fileURLToPath(new URL('../dist/', import.meta.url));
test('the real static build passes the blocking SEO, schema, asset, privacy and CSP gate', async () => {
  await validateOutput(output, process.env.SITE_BUILD_MODE ?? 'private-preview', process.env.GOOGLE_SITE_VERIFICATION);
});
test('mode selection fails closed on unknown, blank, conflicting fixture and invalid verification values', () => {
  assert.equal(readSiteSettings({}).mode, 'private-preview');
  assert.equal(readSiteSettings({ SITE_BUILD_MODE: 'release-candidate' }).indexable, true);
  for (const SITE_BUILD_MODE of ['', 'production', 'public', 'preview']) assert.throws(() => readSiteSettings({ SITE_BUILD_MODE }));
  for (const GOOGLE_SITE_VERIFICATION of ['', ' ', '<script>', 'fake']) assert.throws(() => readSiteSettings({ GOOGLE_SITE_VERIFICATION }));
  assert.equal(readSiteSettings({ GOOGLE_SITE_VERIFICATION: 'synthetic-test-verification-value' }).verification, 'synthetic-test-verification-value');
  assert.throws(() => readSiteSettings({ SITE_BUILD_MODE: 'release-candidate', SITE_TEST_FIXTURE: 'synthetic-release' }));
});
test('structured data reuses the normalized release with no extra API requests', async () => {
  const service = fakeService(completeRelease('3.0.12'));
  const release = await loadRelease({ mode: 'initial-publication' }, service.dependencies);
  const calls = service.calls.length;
  const schema = softwareApplication(release, `${canonicalUrl}_astro/light.png`);
  assert.equal(schema.softwareVersion, '3.0.12');
  assert.deepEqual(schema.downloadUrl, release.release.packages.map(item => item.href));
  assert.equal(schema.license, 'https://github.com/melnorme6/ExtSieve/blob/v3.0.12/LICENSE');
  assert.equal(service.calls.length, calls);
  assert.ok(!('aggregateRating' in schema) && !('review' in schema));
});
for (const status of ['temporarily-unavailable', 'no-stable-release']) {
  test(`structured data omits unavailable release fields for ${status}`, () => {
    const schema = softwareApplication({ status, releasesUrl: 'https://github.com/melnorme6/ExtSieve/releases' }, `${canonicalUrl}_astro/light.png`);
    assert.ok(!('softwareVersion' in schema) && !('downloadUrl' in schema));
  });
}
test('JSON-LD serialization escapes script delimiters and round-trips hostile text', () => {
  const value = { description: '</script><script>alert("test")</script>&\u2028\u2029' };
  const text = serializeJsonLd(value);
  assert.doesNotMatch(text, /<|>|&|\u2028|\u2029/);
  assert.deepEqual(JSON.parse(text), value);
});
test('CSP is moved unchanged ahead of scripts and styles and refuses ambiguous markup', () => {
  const policy = '<meta http-equiv="content-security-policy" content="default-src &#39;none&#39;">';
  const html = `<head><meta charset="utf-8"><script>exact bytes</script>${policy}</head>`;
  assert.equal(orderCsp(html), `<head><meta charset="utf-8">${policy}<script>exact bytes</script></head>`);
  assert.throws(() => orderCsp(html.replace(policy, '')));
  assert.throws(() => orderCsp(html.replace(policy, policy + policy)));
});
test('the gate rejects broken canonical, sitemap, indexing, CSP, assets and schema', async () => {
  const parent = resolve(process.env.SITE_TEST_OUTPUT_ROOT || fileURLToPath(new URL('../test-results/', import.meta.url)), 'gate-fixtures');
  await mkdir(parent, { recursive: true });
  const scratch = await mkdtemp(resolve(parent, 'run-'));
  const original = await readFile(resolve(output, 'index.html'), 'utf8');
  const sitemap = await readFile(resolve(output, 'sitemap.xml'), 'utf8');
  for (const entry of await readdir(output, { withFileTypes: true })) {
    if (entry.name === 'index.html' || entry.name === 'sitemap.xml') continue;
    await cp(resolve(output, entry.name), resolve(scratch, entry.name), { recursive: true });
  }
  await writeFile(resolve(scratch, 'sitemap.xml'), sitemap);
  const mutations = [
    original.replace('rel="canonical" href="' + canonicalUrl, 'rel="canonical" href="https://example.invalid/'),
    original.replace('og:type" content="website', 'og:type" content="invalid'),
    original.replace('"@type":"SoftwareApplication"', '"@type":"Other"'),
    original.replace("connect-src &#39;none&#39;", "connect-src *").replace("connect-src 'none'", 'connect-src *'),
    original.replace(/fonts\/InterVariable[^"\s]*\.woff2/, 'fonts/missing.woff2'),
    original.replace('})();', '})(); /* changed hash */'),
  ];
  for (const html of mutations) {
    assert.notEqual(html, original, 'Mutation must change output');
    await writeFile(resolve(scratch, 'index.html'), html);
    await assert.rejects(validateOutput(scratch, process.env.SITE_BUILD_MODE ?? 'private-preview', process.env.GOOGLE_SITE_VERIFICATION));
  }
  await writeFile(resolve(scratch, 'index.html'), original);
  await writeFile(resolve(scratch, 'sitemap.xml'), sitemap.replace(canonicalUrl, 'http://localhost/'));
  await assert.rejects(validateOutput(scratch, 'private-preview'));
  await writeFile(resolve(scratch, 'sitemap.xml'), sitemap);
  if (!process.env.SITE_BUILD_MODE || process.env.SITE_BUILD_MODE === 'private-preview') {
    await assert.rejects(validateOutput(scratch, 'release-candidate'), /Candidate must be indexable/);
  }
  assert.ok(scratch.startsWith(resolve(parent)));
  let files = 0, bytes = 0;
  async function inventory(directory) {
    const { stat } = await import('node:fs/promises');
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      assert.ok(!entry.isSymbolicLink());
      const path = resolve(directory, entry.name);
      if (entry.isDirectory()) await inventory(path);
      else { files++; bytes += (await stat(path)).size; }
    }
  }
  await inventory(scratch);
  await rm(scratch, { recursive: true });
  await writeFile(resolve(parent, 'cleanup.json'), JSON.stringify({ classification: 'task-owned gate mutation fixtures', files, bytes, removed: true }) + '\n');
});
