import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile, stat } from 'node:fs/promises';
import { resolve, sep } from 'node:path';
import { canonicalUrl, publicBase, socialImageUrl } from '../src/lib/site-settings.ts';
import { en } from '../src/content/en.ts';

export const attributes = tag => Object.fromEntries([...tag.matchAll(/([\w:-]+)="([^"]*)"/g)]
  .map(match => [match[1], match[2].replaceAll('&amp;', '&').replaceAll('&#39;', "'")
    .replaceAll('&quot;', '"').replaceAll('&lt;', '<').replaceAll('&gt;', '>')]));

export async function validateOutput(directory, mode, verification) {
  assert.ok(['private-preview', 'release-candidate'].includes(mode), 'Explicit gate mode required');
  const output = resolve(directory);
  const html = await readFile(resolve(output, 'index.html'), 'utf8');
  const metas = [...html.matchAll(/<meta\b[^>]*>/g)].map(match => attributes(match[0]));
  const meta = key => {
    const values = metas.filter(item => item.name === key || item.property === key);
    assert.equal(values.length, 1, `Exactly one ${key} metadata value required`);
    return values[0].content;
  };
  async function local(url) {
    const targetUrl = new URL(url, canonicalUrl);
    assert.equal(targetUrl.origin, new URL(canonicalUrl).origin, `External resource: ${url}`);
    assert.ok(targetUrl.pathname.startsWith(publicBase), `Asset escapes base: ${url}`);
    const target = resolve(output, decodeURIComponent(targetUrl.pathname.slice(publicBase.length)) || 'index.html');
    assert.ok(target.startsWith(`${output}${sep}`), 'Asset escapes output directory');
    assert.ok((await stat(target)).isFile(), `Missing asset: ${url}`);
    return target;
  }
  assert.match(html, /<html\b[^>]*lang="en"/);
  assert.match(html, /<meta charset="utf-8"/);
  assert.equal(meta('viewport'), 'width=device-width, initial-scale=1');
  assert.equal(html.match(/<title>([^]*?)<\/title>/)?.[1].replaceAll('&amp;', '&'), en.title);
  assert.equal(meta('description'), en.description);
  const links = [...html.matchAll(/<link\b[^>]*>/g)].map(match => attributes(match[0]));
  assert.equal(links.filter(item => item.rel === 'canonical').length, 1);
  assert.equal(links.find(item => item.rel === 'canonical').href, canonicalUrl);
  assert.equal(links.find(item => item.rel === 'sitemap')?.href, `${publicBase}sitemap.xml`);
  assert.equal([...html.matchAll(/<h1\b/g)].length, 1);
  assert.equal(html.match(/<h1\b[^>]*>([^]*?)<\/h1>/)?.[1], en.hero.title);
  const headings = [...html.matchAll(/<h([1-6])\b/g)].map(match => Number(match[1]));
  headings.forEach((level, index) => assert.ok(index === 0 || level <= headings[index - 1] + 1, 'Heading level skipped'));
  const robots = metas.filter(item => ['robots', 'googlebot'].includes(item.name));
  if (mode === 'release-candidate') {
    assert.ok(robots.every(item => !/noindex|nofollow/i.test(item.content)), 'Candidate must be indexable');
    assert.doesNotMatch(html, /Development preview|Development placeholder|screenshot-placeholder|Synthetic release|Automated test fixture/);
  } else {
    assert.equal(meta('robots'), 'noindex, nofollow');
    assert.match(html, /Development preview/);
  }
  const google = metas.filter(item => item.name === 'google-site-verification');
  assert.equal(google.length, verification === undefined ? 0 : 1, 'Verification tag must be optional');
  if (verification !== undefined) assert.equal(google[0].content, verification);
  const sitemap = await readFile(await local(`${publicBase}sitemap.xml`), 'utf8');
  assert.equal(sitemap.trim(), `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><url><loc>${canonicalUrl}</loc></url></urlset>`);
  assert.equal(meta('og:type'), 'website');
  for (const key of ['og:title', 'twitter:title']) assert.equal(meta(key), en.title);
  for (const key of ['og:description', 'twitter:description']) assert.equal(meta(key), en.description);
  assert.equal(meta('og:url'), canonicalUrl);
  assert.equal(meta('twitter:card'), 'summary_large_image');
  for (const key of ['og:image', 'twitter:image']) assert.equal(meta(key), socialImageUrl);
  for (const key of ['og:image:alt', 'twitter:image:alt']) assert.ok(meta(key).includes(en.hero.title));
  const social = await readFile(await local(socialImageUrl));
  assert.equal(social.subarray(1, 4).toString(), 'PNG');
  assert.equal(social.readUInt32BE(16), Number(meta('og:image:width')));
  assert.equal(social.readUInt32BE(20), Number(meta('og:image:height')));
  assert.equal(social.readUInt32BE(16), 1200);
  assert.equal(social.readUInt32BE(20), 630);
  assert.ok(social.byteLength < 150000, 'Social image unexpectedly large');
  const scripts = [...html.matchAll(/<script\b([^>]*)>([^]*?)<\/script>/g)];
  const schemas = scripts.filter(script => attributes(script[0]).type === 'application/ld+json');
  assert.equal(schemas.length, 1);
  const schema = JSON.parse(schemas[0][2]);
  assert.equal(schema['@type'], 'SoftwareApplication');
  assert.equal(schema['@context'], 'https://schema.org');
  assert.equal(schema.url, canonicalUrl);
  assert.equal(schema.name, en.name);
  assert.equal(schema.description, en.description);
  assert.equal(schema.applicationCategory, 'UtilitiesApplication');
  assert.deepEqual(schema.operatingSystem, ['Windows x64', 'Linux x64 (glibc)']);
  assert.deepEqual(schema.featureList, en.features.cards.map(card => card.description));
  assert.deepEqual(schema.offers, { '@type': 'Offer', price: 0, priceCurrency: 'USD' });
  assert.equal(schema.image, socialImageUrl);
  await local(schema.screenshot);
  assert.doesNotMatch(schemas[0][2], /<|aggregateRating|ratingValue|reviewCount|award|downloadCount/);
  const downloads = [...html.matchAll(/<a class="package-row"[^>]*>/g)].map(match => attributes(match[0]).href);
  const version = html.match(/class="release-version">v([^<]+)</)?.[1];
  if (mode === 'release-candidate' || version) {
    assert.match(version ?? '', /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/);
    assert.equal(downloads.length, 5, 'All official packages required');
    const names = [`ExtSieve-Setup-${version}-win-x64.exe`, `ExtSieve-${version}-win-x64.zip`,
      `extsieve_${version}_amd64.deb`, `extsieve-${version}-1.x86_64.rpm`, `ExtSieve-${version}-linux-x64.tar.gz`];
    assert.deepEqual(downloads, names.map(name => `https://github.com/melnorme6/ExtSieve/releases/download/v${version}/${name}`));
    assert.equal(schema.softwareVersion, version);
    assert.deepEqual(schema.downloadUrl, downloads);
    assert.equal(schema.license, `https://github.com/melnorme6/ExtSieve/blob/v${version}/LICENSE`);
  } else {
    assert.equal(downloads.length, 0);
    assert.ok(!('softwareVersion' in schema) && !('downloadUrl' in schema));
  }
  const ids = new Set([...html.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]));
  for (const match of html.matchAll(/<(?:a|link|img|script|source)\b[^>]*>/g)) {
    const item = attributes(match[0]);
    for (const url of [item.href, item.src, ...(item.srcset?.split(',').map(value => value.trim().split(/\s+/)[0]) ?? [])].filter(Boolean)) {
      if (url.startsWith('#')) assert.ok(ids.has(url.slice(1)), `Missing anchor: ${url}`);
      else if (item.rel !== 'canonical' && !(match[0].startsWith('<a ') && url.startsWith('https://'))) await local(url);
    }
  }
  assert.doesNotMatch(html, /<iframe\b|<form\b|\son\w+=|\sstyle=|localhost|127\.0\.0\.1|api\.github\.com/i);
  for (const link of links.filter(item => item.rel === 'stylesheet')) {
    const css = await readFile(await local(link.href), 'utf8');
    assert.doesNotMatch(css, /@import\s|https?:\/\//);
    for (const match of css.matchAll(/url\(["']?([^"')]+)["']?\)/g)) await local(new URL(match[1], new URL(link.href, canonicalUrl)).href);
  }
  const policies = metas.filter(item => item['http-equiv']?.toLowerCase() === 'content-security-policy');
  assert.equal(policies.length, 1);
  const policy = policies[0].content;
  assert.ok(html.toLowerCase().indexOf('http-equiv="content-security-policy"') < html.search(/<(?:script|link)\b/), 'CSP must precede resources');
  assert.doesNotMatch(policy, /unsafe-inline|unsafe-eval|frame-ancestors|report-uri|report-to|sandbox/);
  for (const directive of ["default-src 'none'", "connect-src 'none'", "base-uri 'none'", "object-src 'none'",
    "form-action 'none'", "font-src 'self'", "img-src 'self'", "script-src-attr 'none'", "style-src-attr 'none'"]) {
    assert.ok(policy.includes(directive), `Missing CSP directive: ${directive}`);
  }
  for (const script of scripts) {
    const src = attributes(script[0]).src;
    if (src) {
      const source = await readFile(await local(src), 'utf8');
      assert.doesNotMatch(source, /api\.github\.com|\bfetch\s*\(|XMLHttpRequest|sendBeacon|document\.cookie/);
    } else assert.ok(policy.includes(`'sha256-${createHash('sha256').update(script[2]).digest('base64')}'`), 'Inline script hash mismatch');
  }
  await local(`${publicBase}fonts/OFL.txt`);
  await local(`${publicBase}licenses/tabler-icons-LICENSE.txt`);
  return { mode, version: version ?? null, downloads: downloads.length, inlineScripts: scripts.filter(script => !attributes(script[0]).src).length };
}
