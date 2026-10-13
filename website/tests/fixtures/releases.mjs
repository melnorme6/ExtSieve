import { createHash } from 'node:crypto';

export const api = 'https://api.github.com/repos/melnorme6/ExtSieve';
export const web = 'https://github.com/melnorme6/ExtSieve';
const hash = value => createHash('sha256').update(value).digest('hex');

export function completeRelease(version = '2.4.7', id = 84) {
  const tag = `v${version}`;
  const names = [
    `ExtSieve-Setup-${version}-win-x64.exe`, `ExtSieve-${version}-win-x64.zip`,
    `extsieve_${version}_amd64.deb`, `extsieve-${version}-1.x86_64.rpm`,
    `ExtSieve-${version}-linux-x64.tar.gz`,
  ];
  const checksumText = names.map(name => `${hash(name)}  ${name}\n`).join('');
  const assets = [...names, 'SHA256SUMS'].map((name, index) => ({
    id: id * 10 + index + 1,
    url: `${api}/releases/assets/${id * 10 + index + 1}`,
    name, state: 'uploaded', size: name === 'SHA256SUMS' ? Buffer.byteLength(checksumText) : 1024 + index,
    content_type: 'application/octet-stream', digest: `sha256:${hash(name === 'SHA256SUMS' ? checksumText : name)}`,
    browser_download_url: `${web}/releases/download/${tag}/${name}`,
  }));
  return {
    payload: { id, tag_name: tag, name: `ExtSieve ${version}`, draft: false, prerelease: false,
      url: `${api}/releases/${id}`, html_url: `${web}/releases/tag/${tag}`, assets_url: `${api}/releases/${id}/assets`,
      published_at: '2026-09-18T11:46:21Z', assets },
    checksumText,
    requirements: { type: 'file', path: 'LINUX_REQUIREMENTS.md', size: 1024, html_url: `${web}/blob/${tag}/LINUX_REQUIREMENTS.md` },
  };
}

export function fakeService(fixture = completeRelease(), overrides = {}) {
  const calls = [], warnings = [], waits = [];
  const response = data => new Response(JSON.stringify(data), { headers: { 'content-type': 'application/json' } });
  return {
    calls, warnings, waits,
    dependencies: {
      warn: message => warnings.push(message), wait: async milliseconds => { waits.push(milliseconds); },
      fetch: async (url, options) => {
        calls.push({ url, options });
        if (Object.hasOwn(overrides, url)) {
          const result = overrides[url];
          if (typeof result === 'function') return result(url, options);
          return response(result);
        }
        if (url === `${api}/releases/latest` || url === `${api}/releases/${fixture.payload.id}`) return response(fixture.payload);
        if (url === `${api}/contents/LINUX_REQUIREMENTS.md?ref=${fixture.payload.tag_name}`) return response(fixture.requirements);
        if (url === fixture.payload.assets.find(item => item.name === 'SHA256SUMS')?.browser_download_url) return new Response(fixture.checksumText);
        throw new Error(`Unexpected request in synthetic fixture: ${url}`);
      },
    },
  };
}
