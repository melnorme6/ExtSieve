import { en } from '../content/en';
import type { ReleaseResult } from './releases';

export interface PackageLink { label: string; href: string; }

// Presentation only. The release provider is responsible for validating every value.
export interface DownloadPresentation {
  status: ReleaseResult['status'];
  releasesUrl: string;
  requirementsUrl: string;
  version?: string;
  windows: readonly PackageLink[];
  linux: readonly PackageLink[];
}

export function presentDownloads(result: ReleaseResult): DownloadPresentation {
  if (result.status !== 'ready') return {
    status: result.status, releasesUrl: result.releasesUrl,
    requirementsUrl: en.download.requirements.href, windows: [], linux: [],
  };
  const links = (platform: 'windows' | 'linux') => result.release.packages
    .filter(item => item.platform === platform)
    .map(item => ({ label: en.download.packages[item.kind], href: item.href }));
  return { status: 'ready', version: result.release.version, releasesUrl: result.releasesUrl,
    requirementsUrl: result.release.requirementsUrl, windows: links('windows'), linux: links('linux') };
}
