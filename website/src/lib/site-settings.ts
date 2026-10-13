export const publicOrigin = 'https://melnorme6.github.io';
export const publicBase = '/ExtSieve/';
export const canonicalUrl = `${publicOrigin}${publicBase}`;
export const socialImageUrl = `${canonicalUrl}social/extsieve-og.png`;

export type SiteMode = 'private-preview' | 'release-candidate';

export function readSiteSettings(environment: Record<string, string | undefined>) {
  const mode = environment.SITE_BUILD_MODE ?? 'private-preview';
  if (mode !== 'private-preview' && mode !== 'release-candidate') {
    throw new Error('SITE_BUILD_MODE must be private-preview or release-candidate.');
  }
  const fixture = environment.SITE_TEST_FIXTURE;
  if (fixture !== undefined && (fixture !== 'synthetic-release' || mode !== 'private-preview')) {
    throw new Error('Synthetic release fixtures are only allowed in private-preview mode.');
  }
  const verification = environment.GOOGLE_SITE_VERIFICATION;
  if (verification !== undefined && !/^[A-Za-z0-9_-]{20,256}$/.test(verification)) {
    throw new Error('GOOGLE_SITE_VERIFICATION must be a nonempty HTML verification value.');
  }
  return { mode, verification, fixture: fixture !== undefined, indexable: mode === 'release-candidate' } as const;
}
