import { en } from '../content/en.ts';
import type { ReleaseResult } from './releases';
import { canonicalUrl, socialImageUrl } from './site-settings.ts';

export function softwareApplication(result: ReleaseResult, screenshotUrl: string) {
  return {
    '@context': 'https://schema.org', '@type': 'SoftwareApplication',
    name: en.name, description: en.description, url: canonicalUrl, image: socialImageUrl,
    applicationCategory: 'UtilitiesApplication',
    operatingSystem: ['Windows x64', 'Linux x64 (glibc)'],
    featureList: en.features.cards.map(card => card.description), screenshot: screenshotUrl,
    license: result.status === 'ready'
      ? `https://github.com/melnorme6/ExtSieve/blob/${result.release.tag}/LICENSE`
      : en.footer.links.find(link => link.label === 'License')!.href,
    offers: { '@type': 'Offer', price: 0, priceCurrency: 'USD' },
    ...(result.status === 'ready' ? {
      softwareVersion: result.release.version,
      downloadUrl: result.release.packages.map(item => item.href),
    } : {}),
  };
}

// Keep JSON inside its inert script element, including text containing HTML delimiters.
export function serializeJsonLd(value: unknown): string {
  return JSON.stringify(value).replace(/</g, '\\u003c').replace(/>/g, '\\u003e')
    .replace(/&/g, '\\u0026').replace(/\u2028/g, '\\u2028').replace(/\u2029/g, '\\u2029');
}
