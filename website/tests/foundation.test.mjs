import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile, readdir, stat } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { resolve, sep } from 'node:path';
import test from 'node:test';

const output = resolve(fileURLToPath(new URL('../dist/', import.meta.url)));
const html = await readFile(resolve(output, 'index.html'), 'utf8');
const base = '/ExtSieve/';
const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map((match) => match[1]);
const attributes = (tag) => Object.fromEntries(
  [...tag.matchAll(/([\w:-]+)="([^"]*)"/g)].map((match) => [match[1], match[2].replaceAll('&amp;', '&')]),
);

function localFile(url) {
  assert.ok(url.startsWith(base), `Asset or route escapes project base: ${url}`);
  const pathname = decodeURIComponent(new URL(url, 'https://example.invalid').pathname);
  const relative = pathname.slice(base.length);
  const target = resolve(output, relative || 'index.html');
  assert.ok(target.startsWith(`${output}${sep}`), `Asset escapes output directory: ${url}`);
  return target;
}

test('the static page has English landmarks, one H1, unique IDs and a working skip link', () => {
  assert.match(html, /<!doctype html>/i);
  assert.match(html, /<html\b[^>]*lang="en"/);
  for (const landmark of ['header', 'nav', 'main', 'footer']) assert.match(html, new RegExp(`<${landmark}\\b`));
  assert.equal([...html.matchAll(/<h1\b/g)].length, 1);
  assert.equal(new Set(ids).size, ids.length);
  assert.match(html, /href="#main-content"/);
  assert.ok(ids.includes('main-content'));
  for (const match of html.matchAll(/aria-labelledby="([^"]+)"/g)) {
    for (const id of match[1].split(' ')) assert.ok(ids.includes(id), `Missing accessible label: ${id}`);
  }
});

test('every internal link, anchor and asset resolves beneath /ExtSieve/', async () => {
  for (const tag of html.matchAll(/<(?:a|link|img|script|source)\b[^>]*>/g)) {
    const attrs = attributes(tag[0]);
    const url = attrs.href ?? attrs.src ?? attrs.srcset;
    if (!url) continue;
    if (url.startsWith('#')) {
      assert.ok(ids.includes(url.slice(1)), `Missing anchor: ${url}`);
    } else if (!url.startsWith('https://')) {
      assert.ok((await stat(localFile(url))).isFile(), `Missing local file: ${url}`);
    }
  }
});

test('the UI uses the correct canonical and the explicitly selected indexing mode', () => {
  const links = [...html.matchAll(/<link\b[^>]*>/g)].map((match) => attributes(match[0]));
  assert.equal(links.find((link) => link.rel === 'canonical')?.href, 'https://melnorme6.github.io/ExtSieve/');
  const metas = [...html.matchAll(/<meta\b[^>]*>/g)].map((match) => attributes(match[0]));
  if (process.env.SITE_BUILD_MODE === 'release-candidate') {
    assert.doesNotMatch(metas.find((meta) => meta.name === 'robots')?.content ?? '', /noindex|nofollow/);
    assert.doesNotMatch(html, /Development preview/);
  } else {
    assert.match(metas.find((meta) => meta.name === 'robots')?.content ?? '', /noindex/);
    assert.match(html, /Development preview/);
  }
  assert.doesNotMatch(html, /Development placeholder|screenshot-placeholder/);
});

test('stylesheets and all CSS resources are local and use the project base', async () => {
  const styles = [...html.matchAll(/<link\b[^>]*>/g)]
    .map((match) => attributes(match[0])).filter((link) => link.rel === 'stylesheet');
  assert.ok(styles.length > 0);
  let fontReferenced = false;
  for (const style of styles) {
    const css = await readFile(localFile(style.href), 'utf8');
    assert.doesNotMatch(css, /@import\s|https?:\/\//);
    for (const match of css.matchAll(/url\(["']?([^"')]+)["']?\)/g)) {
      const resource = new URL(match[1], new URL(style.href, 'https://example.invalid'));
      assert.equal(resource.origin, 'https://example.invalid');
      assert.ok((await stat(localFile(resource.pathname))).isFile());
      if (resource.pathname === `${base}fonts/InterVariable-latin.woff2`) fontReferenced = true;
    }
  }
  assert.ok(fontReferenced, 'The local Inter font must be used by the built CSS');
});

test('source font URLs resolve beneath the dev base and match the page preload', async () => {
  // Dev injects source CSS; a successful build can hide a root-relative font 404.
  const devPage = new URL(base, 'https://example.invalid');
  const links = [...html.matchAll(/<link\b[^>]*>/g)].map((match) => attributes(match[0]));
  const preload = links.find((link) => link.rel === 'preload' && link.as === 'font');
  const fontStyle = links.find((link) => link.rel === 'stylesheet' && link.href.startsWith(`${base}fonts/`));
  assert.ok(preload, 'The page must preload its local font');
  assert.ok(fontStyle, 'The page must load the public font stylesheet');
  const sources = [
    [new URL('../src/styles/global.css', import.meta.url), devPage],
    [new URL(`../public/${fontStyle.href.slice(base.length)}`, import.meta.url), new URL(fontStyle.href, devPage)],
  ];
  let faces = 0;
  for (const [file, stylesheetUrl] of sources) {
    const css = await readFile(file, 'utf8');
    for (const face of css.matchAll(/@font-face\s*\{[^}]*\}/g)) {
      const sourceUrl = face[0].match(/url\(["']?([^"')]+)["']?\)/)?.[1];
      assert.ok(sourceUrl, 'Source CSS must declare a font resource');
      const resolvedFont = new URL(sourceUrl, stylesheetUrl);
      assert.equal(resolvedFont.origin, devPage.origin);
      if (face[0].includes("font-family: 'Inter';")) {
        assert.equal(resolvedFont.pathname, preload.href, 'Dev CSS and preload must request the same base-aware asset');
      } else {
        assert.ok(face[0].includes("font-family: 'Inter Complete';"));
        assert.equal(resolvedFont.pathname, `${base}fonts/InterVariable.woff2`, 'The complete upstream font remains the fallback');
      }
      assert.ok((await stat(localFile(resolvedFont.pathname))).isFile());
      faces++;
    }
  }
  assert.equal(faces, 2, 'One Latin face and one complete fallback');
});

test('Inter, its complete license and the approved mark retain exact pinned bytes', async () => {
  const assets = [
    ['fonts/InterVariable.woff2', '693b77d4f32ee9b8bfc995589b5fad5e99adf2832738661f5402f9978429a8e3'],
    ['fonts/InterVariable-latin.woff2', '42722df2a06cd8a27d46a11abec05d40c65d06aeab8fa99ea205179acca7e270'],
    ['fonts/OFL.txt', '262481e844521b326f5ecd053e59b98c8b2da78c8ee1bdbb6e8174305e54935a'],
    ['icons/extsieve.png', '25f012127579f7a0c045ba3cdaf3e1e91dd25075322230eb27c6932c428e8af0'],
  ];
  for (const [asset, sha] of assets) {
    const bytes = await readFile(localFile(`${base}${asset}`));
    assert.equal(createHash('sha256').update(bytes).digest('hex'), sha, asset);
  }
  const font = await readFile(localFile(`${base}fonts/InterVariable.woff2`));
  assert.equal(font.subarray(0, 4).toString(), 'wOF2');
  const license = await readFile(localFile(`${base}fonts/OFL.txt`), 'utf8');
  assert.match(license, /SIL OPEN FONT LICENSE Version 1\.1/);
  assert.match(license, /DISCLAIMER/);
});

test('the UI uses local scripts and resources without third-party embeds or APIs', () => {
  assert.doesNotMatch(html, /<iframe\b|<form\b/i);
  const scripts = [...html.matchAll(/<script\b[^>]*>/g)].map(match => attributes(match[0]));
  assert.equal(scripts.filter(script => script.type !== 'application/ld+json').length, 2, 'One early theme bootstrap and one interaction module');
  for (const match of html.matchAll(/<(?:link|img|script)\b[^>]*>/g)) {
    const attrs = attributes(match[0]);
    if (attrs.rel === 'canonical') continue;
    const url = attrs.href ?? attrs.src;
    if (url) assert.ok(url.startsWith(base), 'Resource must be hosted locally');
  }
});

test('download rows expose a complete validated release or a truthful fallback without fake links', () => {
  assert.match(html, /href="https:\/\/github\.com\/melnorme6\/ExtSieve\/releases"/);
  assert.doesNotMatch(html, /api\.github\.com/);
  assert.doesNotMatch(html, /saved profiles|folder exclusions/i);
  const section = html.match(/<section id="download"[^]*?<\/section>/)?.[0] ?? '';
  assert.doesNotMatch(section, /format-list|download-pending|Expected formats|class="button button-primary"/);
  const version = section.match(/class="release-version">v([^<]+)</)?.[1];
  if (!version) {
    assert.doesNotMatch(section, /package-row|releases\/download\//);
    assert.match(section, /Download information is temporarily unavailable|No stable release is available yet/);
    return;
  }
  assert.match(version, /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/);
  assert.equal([...section.matchAll(/class="card download-card"/g)].length, 2);
  assert.equal([...section.matchAll(/class="architecture-badge">x64</g)].length, 2);
  const lists = [...section.matchAll(/<ul class="package-links"[^>]*>([^]*?)<\/ul>/g)];
  assert.deepEqual(lists.map(list => [...list[1].matchAll(/class="package-row"/g)].length), [2, 3]);
  const rows = [...section.matchAll(/<a class="package-row"[^>]*>([^]*?)<\/a>/g)];
  const labels = ['Installer .exe', 'Portable .zip', 'Debian package .deb', 'RPM package .rpm', 'Portable archive .tar.gz'];
  const names = [`ExtSieve-Setup-${version}-win-x64.exe`, `ExtSieve-${version}-win-x64.zip`, `extsieve_${version}_amd64.deb`, `extsieve-${version}-1.x86_64.rpm`, `ExtSieve-${version}-linux-x64.tar.gz`];
  rows.forEach((row, index) => {
    assert.equal(attributes(row[0]).href, `https://github.com/melnorme6/ExtSieve/releases/download/v${version}/${names[index]}`);
    assert.ok(row[1].includes(`<span>${labels[index]}</span>`));
    assert.equal([...row[1].matchAll(/<svg\b/g)].length, 1);
  });
  assert.ok(section.includes(`href="https://github.com/melnorme6/ExtSieve/blob/v${version}/LINUX_REQUIREMENTS.md"`));
});

test('browser bundles contain no release-provider code or GitHub API requests', async () => {
  for (const tag of html.matchAll(/<script\b[^>]*>/g)) {
    const src = attributes(tag[0]).src;
    if (!src) continue;
    const script = await readFile(localFile(src), 'utf8');
    assert.doesNotMatch(script, /api\.github\.com|loadRelease|release-published|SHA256SUMS|\bfetch\s*\(/);
  }
});

test('the output contains no source maps, source modules or dependency/build caches', async () => {
  async function inspect(directory) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      assert.ok(!['node_modules', '.astro', 'src', 'tests'].includes(entry.name));
      const path = resolve(directory, entry.name);
      if (entry.isDirectory()) await inspect(path);
      else assert.ok(!/\.(?:map|ts|astro|mjs)$/.test(entry.name), `Unexpected source: ${entry.name}`);
    }
  }
  await inspect(output);
});
