import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import test from 'node:test';

const expected = {
  'download.svg': 'c13f6f9cf87deee99e331622169efd59cd764b376994540347e50d6bb5e2e0ef',
  'archive.svg': 'f6a87827b2402644e13e854c6c0574232212b98e66e8eb51dde3589d4dcc6ffa',
  'arrow-right.svg': '7e602a26d53e78ceb788f38cb89ec1d1091130c56d7829ca294b7ed0642277c6',
  'arrows-maximize.svg': '106923855401d2164938e7db50f20c6a47c90d8f4aa4e299b94792ea87dd75e5',
  'brand-windows.svg': 'b54e58c6763339fa211a3aca624ba052e7a397556d427c867da6a5873fdd305e',
  'external-link.svg': '95de8d4e3e77d34b39a94e157ff26b5a860015c6ce3056794f63dd3a55805bbc',
  'filter.svg': '69bbcca665588bd0b64c44c42ced4538b3e3c75a59e3fc46e58b1f10d99d15ec',
  'menu-2.svg': 'bcef199c5e53082a56c7bf33d4c48181b71373eb5d923cce8884aca09d106842',
  'moon.svg': '7cabaae40eea65310da478d3cad30b23cf1d7e99adad56378f82824338079668',
  'scan.svg': '4e64112a98f6efa9ddfcb6486aa12d25867cf9d7eb13e51c8608d8bc12740586',
  'sun.svg': '6677490605af8987d55d6e4185b1f6c241eced65b63ccfece68cadb7ce90fbb5',
  'terminal-2.svg': 'ea232ff7461f39a6c65c5919c0f9b28de29d2586730a9eeef2b2428526ceb069',
  'x.svg': '9350785360414c39450c3505f17d1745e55a7534edace8548e30cf49e3727d43',
  'zoom-in.svg': 'b361e358c8d0035b2b822b2b497f83b60a00c31a6300b502838292c706070c4e',
  'device-desktop.svg': '2fdd2150904a844185250f26453a02f31ee9d77a1f936a1df9c2ecd77cfc788d',
  'user-off.svg': '3cc11a387ee367ab683b8fe4537279a6191257fa109737c0642f2f05e4022376',
  'chart-bar-off.svg': 'c577e531b54f24f1ca1d801a2b03f9f1d70a2518f3903e83de98649f2413b34e',
  'license.svg': '014b4eec59adb9fe0c8a6c0da3d3c6452148d9bcb74f5cd6c8d6bf48000772a1',
};

test('the local Tabler subset preserves pinned upstream bytes and passive SVG geometry', async () => {
  const directory = new URL('../src/assets/icons/tabler/', import.meta.url);
  assert.deepEqual((await readdir(directory)).sort(), Object.keys(expected).sort());
  for (const [file, hash] of Object.entries(expected)) {
    const bytes = await readFile(new URL(file, directory));
    assert.equal(createHash('sha256').update(bytes).digest('hex'), hash, file);
    const svg = bytes.toString('utf8');
    assert.match(svg, /viewBox="0 0 24 24"/);
    assert.match(svg, /stroke="currentColor"/);
    assert.match(svg, /stroke-width="2"/);
    assert.doesNotMatch(svg, /<!DOCTYPE|<script|<foreignObject|\son\w+=|\bhref=|url\(|<style/i);
  }
});

test('complete Tabler MIT notice ships byte-for-byte with no runtime icon dependency', async () => {
  const source = await readFile(new URL('../public/licenses/tabler-icons-LICENSE.txt', import.meta.url));
  const built = await readFile(new URL('../dist/licenses/tabler-icons-LICENSE.txt', import.meta.url));
  assert.equal(createHash('sha256').update(source).digest('hex'), 'b740a1d46122672da62833e97f7e7c8a13fa85cbc7445b584b297cc00dde93db');
  assert.deepEqual(built, source);
  const html = await readFile(new URL('../dist/index.html', import.meta.url), 'utf8');
  assert.doesNotMatch(html, /@tabler|cdn\.|unpkg|<use\b|<svg[^>]*tabindex/i);
  assert.match(html, /class="icon" aria-hidden="true"/);
  assert.match(html, /<svg[^>]*stroke-width="2"/);
});
