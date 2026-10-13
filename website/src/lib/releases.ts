import { readSiteSettings } from './site-settings.ts';

export const repository = 'https://github.com/melnorme6/ExtSieve';
export const releasesUrl = `${repository}/releases`;
const api = 'https://api.github.com/repos/melnorme6/ExtSieve';
const headers = { Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2026-03-10' };

export type PackageKind = 'windows-installer' | 'windows-portable' | 'linux-deb' | 'linux-rpm' | 'linux-portable';
export interface ReleaseAsset {
  readonly id: number;
  readonly name: string;
  readonly href: string;
  readonly size: number;
  readonly apiSha256: string | null;
}
export interface ReleasePackage extends ReleaseAsset {
  readonly kind: PackageKind;
  readonly platform: 'windows' | 'linux';
  readonly sha256: string;
}
export interface ValidatedRelease {
  readonly id: number;
  readonly tag: string;
  readonly version: string;
  readonly publishedAt: string;
  readonly href: string;
  readonly requirementsUrl: string;
  readonly packages: readonly ReleasePackage[];
  readonly checksum: ReleaseAsset;
  readonly integrity: { readonly metadataValidated: true; readonly checksumFileValidated: true; readonly checksumFileDigestVerified: boolean; readonly binaryBytesVerified: false };
}
export type ReleaseResult =
  | { readonly status: 'ready'; readonly releasesUrl: string; readonly release: ValidatedRelease }
  | { readonly status: 'temporarily-unavailable' | 'no-stable-release'; readonly releasesUrl: string };

// Callers must validate event provenance and deployment ordering independently.
// These inputs constrain fetched public data; they are not deployment authorization.
export type BuildContext =
  | { readonly mode: 'ordinary' }
  | { readonly mode: 'initial-publication' }
  | { readonly mode: 'release-published'; readonly expectedId: number; readonly expectedTag: string; readonly minimumVersion: string };
export interface ReleaseDependencies {
  fetch?: typeof fetch;
  warn?: (message: string) => void;
  wait?: (milliseconds: number) => Promise<void>;
  timeoutMs?: number;
}

export class ReleaseDataError extends Error {
  readonly code: 'invalid-data' | 'incomplete-release' | 'release-identity' | 'stale-release';
  constructor(code: ReleaseDataError['code'], message: string) {
    super(`Release data: ${message}`);
    this.name = 'ReleaseDataError';
    this.code = code;
  }
}
class TemporaryFailure extends Error {
  readonly retryable: boolean;
  constructor(message: string, retryable = true) { super(message); this.retryable = retryable; }
}
function invalid(message: string): never { throw new ReleaseDataError('invalid-data', message); }
function object(value: unknown, label: string): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) invalid(`${label} must be an object.`);
  return value as Record<string, unknown>;
}
function positiveInteger(value: unknown, label: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value <= 0) invalid(`${label} must be a positive safe integer.`);
  return value;
}
function text(value: unknown, label: string): string {
  if (typeof value !== 'string' || value.length === 0) invalid(`${label} must be a nonempty string.`);
  return value;
}
function exactUrl(value: unknown, expected: string, label: string): string {
  const input = text(value, label);
  let url: URL;
  try { url = new URL(input); } catch { return invalid(`${label} is not a URL.`); }
  if (input !== expected || url.href !== expected || url.protocol !== 'https:' || url.username || url.password || url.search || url.hash) {
    invalid(`${label} does not match the official repository, release or asset.`);
  }
  return input;
}
function stableVersion(value: unknown): string {
  const version = text(value, 'Version');
  if (!/^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(version) ||
      version.split('.').some(part => !Number.isSafeInteger(Number(part)))) invalid('Version must be stable SemVer without build metadata.');
  return version;
}
function tagVersion(value: unknown): string {
  const tag = text(value, 'Release tag');
  if (!tag.startsWith('v')) invalid('Release tag must start with v.');
  return stableVersion(tag.slice(1));
}
export function compareVersions(left: string, right: string): number {
  const a = stableVersion(left).split('.').map(Number), b = stableVersion(right).split('.').map(Number);
  for (let i = 0; i < 3; i++) { const delta = a[i]! - b[i]!; if (delta) return Math.sign(delta); }
  return 0;
}
export function packageContract(version: string): readonly { kind: PackageKind; platform: 'windows' | 'linux'; name: string }[] {
  stableVersion(version);
  return [
    { kind: 'windows-installer', platform: 'windows', name: `ExtSieve-Setup-${version}-win-x64.exe` },
    { kind: 'windows-portable', platform: 'windows', name: `ExtSieve-${version}-win-x64.zip` },
    { kind: 'linux-deb', platform: 'linux', name: `extsieve_${version}_amd64.deb` },
    { kind: 'linux-rpm', platform: 'linux', name: `extsieve-${version}-1.x86_64.rpm` },
    { kind: 'linux-portable', platform: 'linux', name: `ExtSieve-${version}-linux-x64.tar.gz` },
  ];
}

export function validateReleaseMetadata(payload: unknown) {
  const release = object(payload, 'Release');
  if (release.draft !== false || release.prerelease !== false) invalid('Only published stable releases are accepted.');
  const version = tagVersion(release.tag_name), tag = `v${version}`;
  const id = positiveInteger(release.id, 'Release ID');
  exactUrl(release.url, `${api}/releases/${id}`, 'Release API URL');
  exactUrl(release.assets_url, `${api}/releases/${id}/assets`, 'Assets API URL');
  const href = exactUrl(release.html_url, `${releasesUrl}/tag/${tag}`, 'Release page');
  const publishedAt = text(release.published_at, 'Publication timestamp');
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/.test(publishedAt) ||
      !Number.isFinite(Date.parse(publishedAt)) || new Date(publishedAt).toISOString() !== publishedAt.replace('Z', '.000Z')) invalid('Publication timestamp is invalid.');
  if (!Array.isArray(release.assets) || release.assets.length > 100) invalid('Release assets must be a bounded array.');
  const contract = packageContract(version);
  const expectedNames = new Set([...contract.map(item => item.name), 'SHA256SUMS']);
  const names = new Set<string>(), ids = new Set<number>();
  const assets: ReleaseAsset[] = release.assets.map((value: unknown) => {
    const item = object(value, 'Asset');
    const name = text(item.name, 'Asset name'), assetId = positiveInteger(item.id, 'Asset ID');
    if (!/^[A-Za-z0-9_.-]+$/.test(name) || names.has(name) || ids.has(assetId)) invalid('Asset names and IDs must be safe and unique.');
    names.add(name); ids.add(assetId);
    if (!expectedNames.has(name)) invalid('Asset name is outside the official six-asset contract.');
    exactUrl(item.url, `${api}/releases/assets/${assetId}`, 'Asset API URL');
    const assetHref = exactUrl(item.browser_download_url, `${releasesUrl}/download/${tag}/${name}`, 'Asset download URL');
    if (item.state !== 'uploaded') throw new ReleaseDataError('incomplete-release', 'An asset upload is not complete.');
    const size = positiveInteger(item.size, 'Asset size');
    text(item.content_type, 'Asset MIME type'); // octet-stream is valid; names define formats.
    let apiSha256: string | null = null;
    if (item.digest !== null && item.digest !== undefined) {
      if (typeof item.digest !== 'string' || !/^sha256:[a-f0-9]{64}$/.test(item.digest)) invalid('Asset digest is not SHA-256.');
      apiSha256 = item.digest.slice(7);
    }
    return { id: assetId, name, href: assetHref, size, apiSha256 };
  });
  if (assets.length !== expectedNames.size) throw new ReleaseDataError('incomplete-release', 'Five application packages and SHA256SUMS are required.');
  const checksum = assets.find(asset => asset.name === 'SHA256SUMS')!;
  if (checksum.size > 8192) invalid('SHA256SUMS exceeds its size limit.');
  const packages = contract.map(item => ({ ...assets.find(asset => asset.name === item.name)!, kind: item.kind, platform: item.platform }));
  return { id, tag, version, publishedAt, href, packages, checksum };
}

export async function validateChecksums(bytes: Uint8Array, release: ReturnType<typeof validateReleaseMetadata>): Promise<readonly ReleasePackage[]> {
  if (bytes.byteLength !== release.checksum.size) invalid('SHA256SUMS size contradicts the asset metadata.');
  const hash = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', new Uint8Array(bytes))), byte => byte.toString(16).padStart(2, '0')).join('');
  if (release.checksum.apiSha256 && hash !== release.checksum.apiSha256) invalid('SHA256SUMS digest contradicts the API.');
  let content: string;
  try { content = new TextDecoder('utf-8', { fatal: true }).decode(bytes); } catch { return invalid('SHA256SUMS must be UTF-8.'); }
  if (!content.endsWith('\n')) invalid('SHA256SUMS must end with a newline.');
  const lines = content.slice(0, -1).split('\n'), hashes = new Map<string, string>();
  for (const line of lines) {
    const match = /^([a-f0-9]{64}) {2}([A-Za-z0-9_.-]+)$/.exec(line);
    if (!match || hashes.has(match[2]!)) invalid('SHA256SUMS contains malformed or duplicate entries.');
    hashes.set(match[2]!, match[1]!);
  }
  if (hashes.size !== release.packages.length || [...hashes.keys()].some(name => !release.packages.some(item => item.name === name))) {
    invalid('SHA256SUMS must contain exactly the five application packages.');
  }
  return release.packages.map(item => {
    const sha256 = hashes.get(item.name)!;
    if (item.apiSha256 && item.apiSha256 !== sha256) invalid('Package digest contradicts SHA256SUMS.');
    return { ...item, sha256 };
  });
}

export async function loadRelease(context: BuildContext = { mode: 'ordinary' }, dependencies: ReleaseDependencies = {}): Promise<ReleaseResult> {
  if (!['ordinary', 'initial-publication', 'release-published'].includes(context.mode)) invalid('Unknown build context.');
  if (context.mode === 'release-published') {
    positiveInteger(context.expectedId, 'Expected release ID'); tagVersion(context.expectedTag); stableVersion(context.minimumVersion);
  }
  const request = dependencies.fetch ?? fetch;
  const warn = dependencies.warn ?? console.warn;
  const wait = dependencies.wait ?? (milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds)));
  const timeoutMs = dependencies.timeoutMs ?? 8000;
  if (!Number.isSafeInteger(timeoutMs) || timeoutMs < 1 || timeoutMs > 30000) invalid('Request timeout is outside the supported bounds.');

  async function get(url: string, limit: number, checksum = false, allow404 = false): Promise<Uint8Array | null> {
    for (let attempt = 0; attempt < 2; attempt++) {
      try {
        const signal = AbortSignal.timeout(timeoutMs);
        let target = url, response: Response | undefined;
        for (let redirects = 0; redirects <= 3; redirects++) {
          response = await request(target, { headers: checksum ? { Accept: 'application/octet-stream' } : headers, redirect: 'manual', signal });
          if (response.status >= 300 && response.status < 400) {
            const location = response.headers.get('location');
            await response.body?.cancel();
            if (!checksum || !location || redirects === 3) invalid('Unexpected HTTP redirect.');
            let next: URL;
            try { next = new URL(location, target); } catch { return invalid('Checksum delivery has an invalid redirect URL.'); }
            if (next.protocol !== 'https:' || next.username || next.password || next.port || next.hash ||
                !['release-assets.githubusercontent.com', 'objects.githubusercontent.com'].includes(next.hostname)) invalid('Checksum delivery redirects outside official GitHub storage.');
            target = next.href;
          } else break;
        }
        if (!response) invalid('Missing HTTP response.');
        if (response.status === 404 && allow404) { await response.body?.cancel(); return null; }
        const rateLimited = response.status === 429 || (response.status === 403 &&
          (response.headers.get('x-ratelimit-remaining') === '0' || response.headers.has('retry-after')));
        if ([408, 425, 500, 502, 503, 504].includes(response.status) || rateLimited) {
          await response.body?.cancel();
          // Defer a rate-limited build instead of retrying before the reset window.
          throw new TemporaryFailure('Public release service is temporarily unavailable or rate limited.', !rateLimited);
        }
        if (!response.ok) { await response.body?.cancel(); invalid(`Unexpected HTTP status ${response.status}.`); }
        if (!checksum && !/^(application\/json|application\/vnd\.github\+json)(?:;|$)/i.test(response.headers.get('content-type') ?? '')) invalid('API response must be JSON.');
        const declaredSize = response.headers.get('content-length');
        if (declaredSize !== null && (!/^\d+$/.test(declaredSize) || Number(declaredSize) > limit)) invalid('HTTP response exceeds its size limit.');
        if (!response.body) invalid('HTTP response has no body.');
        const reader = response.body.getReader(), chunks: Uint8Array[] = [];
        let size = 0;
        try {
          while (true) {
            const { value, done } = await reader.read(); if (done) break;
            size += value.byteLength;
            if (size > limit) invalid('HTTP response exceeds its size limit.');
            chunks.push(value);
          }
        } finally { await reader.cancel(); reader.releaseLock(); }
        const bytes = new Uint8Array(size); let offset = 0;
        for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
        return bytes;
      } catch (error) {
        if (error instanceof ReleaseDataError) throw error;
        if (!(error instanceof TemporaryFailure) && !(error instanceof TypeError) &&
            !(error instanceof Error && ['TimeoutError', 'AbortError'].includes(error.name))) throw error;
        if (error instanceof TemporaryFailure && !error.retryable) throw error;
        if (attempt === 1) throw new TemporaryFailure('Public release request failed after two bounded attempts.');
        await wait(250);
      }
    }
    throw new TemporaryFailure('Public release request failed.');
  }
  async function json(url: string, allow404 = false): Promise<unknown> {
    const bytes = await get(url, 1024 * 1024, false, allow404);
    if (bytes === null) return undefined;
    try { return JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes)); }
    catch { return invalid('API response contains malformed JSON.'); }
  }
  try {
    const latestPayload = await json(`${api}/releases/latest`, true);
    if (latestPayload === undefined) {
      const repo = object(await json(api), 'Repository');
      if (repo.full_name !== 'melnorme6/ExtSieve' || repo.private !== false) invalid('Expected public repository is unavailable.');
      const listed = await json(`${api}/releases?per_page=100`);
      if (!Array.isArray(listed)) invalid('Release listing must be an array.');
      for (const value of listed) {
        const item = object(value, 'Listed release');
        if (typeof item.draft !== 'boolean' || typeof item.prerelease !== 'boolean') invalid('Release listing has invalid stability flags.');
        if (!item.draft && !item.prerelease) invalid('Latest endpoint contradicts the stable release listing.');
      }
      if (context.mode !== 'ordinary') throw new ReleaseDataError('release-identity', 'A complete stable release is required for publication.');
      return { status: 'no-stable-release', releasesUrl };
    }
    const latest = validateReleaseMetadata(latestPayload);
    let selected = latest;
    if (context.mode === 'release-published') {
      selected = validateReleaseMetadata(await json(`${api}/releases/${context.expectedId}`));
      if (selected.id !== context.expectedId || selected.tag !== context.expectedTag) throw new ReleaseDataError('release-identity', 'Fetched release does not match the expected event.');
      if (compareVersions(selected.version, latest.version) < 0 || compareVersions(selected.version, context.minimumVersion) < 0) throw new ReleaseDataError('stale-release', 'An older release cannot replace a newer site version.');
      if (selected.id !== latest.id || selected.tag !== latest.tag) throw new ReleaseDataError('release-identity', 'Expected release must also be the current latest stable release.');
    }
    const requirements = object(await json(`${api}/contents/LINUX_REQUIREMENTS.md?ref=${selected.tag}`), 'Linux requirements');
    if (requirements.type !== 'file' || requirements.path !== 'LINUX_REQUIREMENTS.md') invalid('Release-tagged Linux requirements are missing.');
    positiveInteger(requirements.size, 'Linux requirements size');
    const requirementsUrl = exactUrl(requirements.html_url, `${repository}/blob/${selected.tag}/LINUX_REQUIREMENTS.md`, 'Linux requirements URL');
    const bytes = await get(selected.checksum.href, 8192, true);
    const packages = await validateChecksums(bytes!, selected);
    return { status: 'ready', releasesUrl, release: { ...selected, packages, requirementsUrl,
      integrity: { metadataValidated: true, checksumFileValidated: true, checksumFileDigestVerified: selected.checksum.apiSha256 !== null, binaryBytesVerified: false } } };
  } catch (error) {
    if (!(error instanceof TemporaryFailure)) throw error;
    if (context.mode !== 'ordinary') throw new ReleaseDataError('release-identity', 'Publication requires available, complete release data; fallback is forbidden.');
    warn('[release-data] Temporary public API/download outage; using the general GitHub Releases link without a version or package URLs.');
    return { status: 'temporarily-unavailable', releasesUrl };
  }
}

export function createReleaseLoader(context: BuildContext, dependencies: ReleaseDependencies = {}): () => Promise<ReleaseResult> {
  let result: Promise<ReleaseResult> | undefined;
  return () => result ??= loadRelease(context, dependencies);
}

export function readBuildContext(environment: Record<string, string | undefined>): BuildContext {
  const settings = readSiteSettings(environment);
  const values = [environment.SITE_EXPECTED_RELEASE_ID, environment.SITE_EXPECTED_RELEASE_TAG, environment.SITE_MINIMUM_VERSION];
  if (values.every(value => value === undefined)) return { mode: settings.indexable ? 'initial-publication' : 'ordinary' };
  if (!settings.indexable || values.some(value => value === undefined)) invalid('Expected release inputs require candidate mode and all three values.');
  const [id, tag, minimum] = values as [string, string, string];
  if (!/^[1-9]\d*$/.test(id)) invalid('Expected release ID must be canonical.');
  const expectedId = positiveInteger(Number(id), 'Expected release ID');
  tagVersion(tag); stableVersion(minimum);
  return { mode: 'release-published', expectedId, expectedTag: tag, minimumVersion: minimum };
}

// One shared promise for this build, also available to structured metadata consumers.
export const getBuildRelease = createReleaseLoader(readBuildContext(process.env));
