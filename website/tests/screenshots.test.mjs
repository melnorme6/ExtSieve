import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { sharp, validateRectangle, cropRectangle, detailRoot, prepareScreenshots } from '../scripts/screenshot-tools.mjs';
import { screenshotSources, screenshotDetails, screenshotDetailsByTheme, screenshotCrops } from '../src/lib/screenshot-config.ts';
import { en } from '../src/content/en.ts';

test('source identities, dimensions and privacy metadata are pinned', async () => {
  for (const source of Object.values(screenshotSources)) {
    const bytes = await readFile(new URL(`../src/assets/screenshots/${source.file}`, import.meta.url));
    assert.equal(createHash('sha256').update(bytes).digest('hex'), source.sha256);
    const metadata = await sharp(bytes).metadata();
    assert.equal(metadata.width, source.width); assert.equal(metadata.height, source.height);
    assert.ok(!metadata.exif && !metadata.xmp && !metadata.iptc && !metadata.comments);
  }
});

test('each detail is the exact source-pixel region without resizing or reconstruction', async () => {
  for (const { id, source, ...crop } of screenshotCrops) {
    const rectangle = cropRectangle(crop);
    const input = new URL(`../src/assets/screenshots/${screenshotSources[source].file}`, import.meta.url);
    const region = await sharp(fileURLToPath(input)).extract(rectangle).ensureAlpha().raw().toBuffer();
    const actual = await sharp(fileURLToPath(new URL(`${id}.png`, detailRoot))).ensureAlpha().raw().toBuffer();
    assert.deepEqual(actual, region, id);
    const webp = await sharp(fileURLToPath(new URL(`${id}.webp`, detailRoot))).ensureAlpha().raw().toBuffer();
    assert.deepEqual(webp, region, `Lossless WebP: ${id}`);
    const bytes = await readFile(new URL(`${id}.png`, detailRoot));
    assert.ok(bytes.length < (await readFile(input)).length, 'The derived detail must transfer fewer bytes than its whole source');
  }
});

test('crop preparation is deterministic and rejects unsafe or out-of-bounds rectangles', async () => {
  const before = await Promise.all(screenshotCrops.map(({ id }) => readFile(new URL(`${id}.webp`, detailRoot))));
  await prepareScreenshots();
  for (const [index, { id }] of screenshotCrops.entries()) assert.deepEqual(await readFile(new URL(`${id}.webp`, detailRoot)), before[index]);
  const source = screenshotSources.light;
  for (const rectangle of [
    { left: -1, top: 0, width: 1, height: 1 }, { left: 0, top: 0, width: 0, height: 1 },
    { left: 0.5, top: 0, width: 1, height: 1 }, { left: 902, top: 0, width: 1, height: 1 },
    { left: 0, top: 732, width: 1, height: 1 },
  ]) assert.throws(() => validateRectangle(rectangle, source));
});

test('early theme bootstrap accepts only known preferences and tolerates blocked storage', async () => {
  const script = await readFile(new URL('../src/scripts/theme-bootstrap.js', import.meta.url), 'utf8');
  for (const [saved, expected] of [[null, undefined], ['dark', 'dark'], ['light', 'light'], ['system', undefined], ['invalid', undefined]]) {
    const document = { documentElement: { dataset: {} } };
    vm.runInNewContext(script, { document, localStorage: { getItem: () => saved } });
    assert.equal(document.documentElement.dataset.theme, expected);
  }
  const document = { documentElement: { dataset: {} } };
  vm.runInNewContext(script, { document, localStorage: { getItem: () => { throw new Error('Storage unavailable'); } } });
  assert.equal(document.documentElement.dataset.theme, undefined);
});

test('built full-window images preserve original bytes and details resolve at the project base', async () => {
  const html = await readFile(new URL('../dist/index.html', import.meta.url), 'utf8');
  for (const source of [screenshotSources.light, screenshotSources.dark]) {
    const basename = source.file.slice(0, -4);
    const url = [...html.matchAll(/src="([^"]+)"/g)].map(match => match[1]).find(url => url.includes(`${basename}.`));
    assert.ok(url?.startsWith('/ExtSieve/_astro/'));
    const bytes = await readFile(new URL(`../dist/${url.slice('/ExtSieve/'.length)}`, import.meta.url));
    assert.equal(createHash('sha256').update(bytes).digest('hex'), source.sha256);
  }
  for (const detail of screenshotCrops) {
    assert.ok(html.includes(`/ExtSieve/screenshots/details/${detail.id}.png`));
    assert.deepEqual(await readFile(new URL(`../dist/screenshots/details/${detail.id}.png`, import.meta.url)), await readFile(new URL(`${detail.id}.png`, detailRoot)));
  }
});

test('lossless full-window WebP preserves every source pixel and improves delivered bytes', async () => {
  for (const key of ['light', 'dark']) {
    const source = await readFile(new URL(`../src/assets/screenshots/${screenshotSources[key].file}`, import.meta.url));
    const webp = await readFile(new URL(`${key}-full.webp`, detailRoot));
    assert.deepEqual(await sharp(webp).ensureAlpha().raw().toBuffer(), await sharp(source).ensureAlpha().raw().toBuffer());
    assert.ok(webp.length < source.length);
  }
});

test('images retain direct full-size access without a theme menu or scroll-only detail controls', async () => {
  const html = await readFile(new URL('../dist/index.html', import.meta.url), 'utf8');
  assert.doesNotMatch(html, /Open full size|detail-scroll|theme-menu|data-theme-choice/);
  assert.equal([...html.matchAll(/class="step-card"/g)].length, 3);
  assert.equal(screenshotDetails.reduce((sum, detail) => sum + detail.frames.length, 0), 6);
  assert.match(html, /Switch to dark mode/);
  assert.match(html, /Switch to light mode/);
  assert.equal([...html.matchAll(/class="image-expand"/g)].length, 10);
  const steps = [...html.matchAll(/<li class="step-card">([\s\S]*?)<\/li>/g)].map(match => match[1]);
  for (const step of steps) {
    assert.equal([...step.matchAll(/<a\b/g)].length, 2, 'One full-context trigger per theme');
    assert.equal([...step.matchAll(/class="image-expand"/g)].length, 2);
    for (const theme of ['light', 'dark']) assert.match(step, new RegExp(`step-images image-open theme-${theme}`));
    assert.match(step, /aria-label="View full-size screenshot:/);
  }
});

test('each step and its modal target use authentic sources for their theme', async () => {
  const html = await readFile(new URL('../dist/index.html', import.meta.url), 'utf8');
  assert.equal(new Set(screenshotCrops.map(crop => crop.id)).size, screenshotCrops.length);
  for (const theme of ['light', 'dark']) {
    const details = screenshotDetailsByTheme[theme];
    assert.equal(details.length, 3);
    for (const detail of details) {
      assert.equal(detail.source === 'dark', theme === 'dark');
      assert.ok(html.includes(`href="/ExtSieve/screenshots/details/${detail.id}.png"`));
      for (const frame of [...detail.frames, ...('mobileFrames' in detail ? detail.mobileFrames : [])]) {
        assert.equal(frame.id.endsWith('-dark'), theme === 'dark');
        assert.ok(screenshotCrops.some(crop => crop.id === frame.id && crop.source === detail.source));
      }
    }
  }
  assert.doesNotMatch(screenshotDetailsByTheme.dark[1].alt, /cs filter is active/);
});

test('the seven approved descriptions remain exact and are included in rendered output', async () => {
  const expected = [
    'Scan folders, select the file extensions you need, and create ZIP archives without modifying your original files. Free, local, and open source.',
    'Scan folders recursively and discover file extensions, grouped with their file counts.',
    'Find and select the file extensions you need, keeping unwanted file types out of your archive.',
    'Create ZIP archives while preserving the original folder structure or flattening files into a single directory.',
    'Select the folder you want to analyze. ExtSieve scans its contents and groups files by extension.',
    'Browse the results, filter extensions, and select the file types you want to include in your archive.',
    'Choose whether to preserve the folder structure or flatten it, then save your ZIP archive.',
  ];
  assert.deepEqual([en.hero.description, ...en.features.cards.map(card => card.description), ...en.howItWorks.steps.map(step => step.description)], expected);
  const html = await readFile(new URL('../dist/index.html', import.meta.url), 'utf8');
  for (const description of expected) assert.ok(html.includes(description));
});
