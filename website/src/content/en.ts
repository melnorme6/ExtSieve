interface Link {
  label: string;
  href: string;
}

interface Card {
  title: string;
  description: string;
}

interface SiteContent {
  name: string;
  title: string;
  description: string;
  navigation: readonly Link[];
  skipLink: string;
  navigationLabel: string;
  previewNotice: string;
  hero: Card & { primaryAction: Link; secondaryAction: Link; trust: readonly string[] };
  features: Card & { cards: readonly Card[] };
  howItWorks: Card & { steps: readonly (Card & { number: string })[] };
  screenshots: { title: string; light: string; dark: string; lightAlt: string; darkAlt: string };
  theme: { toDark: string; toLight: string };
  menu: string;
  gallery: { open: string; close: string; original: string; zoom: string; viewerLabel: string };
  download: Card & { action: Link; requirements: Link; unavailable: string; noStable: string;
    packages: Record<'windows-installer' | 'windows-portable' | 'linux-deb' | 'linux-rpm' | 'linux-portable', string>;
    platforms: readonly { family: 'windows' | 'linux'; name: string; architecture: string }[] };
  footer: { description: string; links: readonly Link[]; license: string };
}

const repository = 'https://github.com/melnorme6/ExtSieve';

export const en = {
  name: 'ExtSieve',
  title: 'ExtSieve — Filter Files by Extension & Create ZIP Archives',
  description:
    'ExtSieve is a free, open-source desktop app for Windows and Linux that scans folders by file extension and creates focused ZIP archives locally.',
  navigation: [
    { label: 'Features', href: '#features' },
    { label: 'Screenshots', href: '#screenshots' },
    { label: 'Download', href: '#download' },
    { label: 'GitHub', href: repository },
  ],
  skipLink: 'Skip to content',
  navigationLabel: 'Main navigation',
  previewNotice: 'Development preview. Final website and release review are pending.',
  hero: {
    title: 'Filter. Organize. Archive.',
    description:
      'Scan folders, select the file extensions you need, and create ZIP archives without modifying your original files. Free, local, and open source.',
    primaryAction: { label: 'Download ExtSieve', href: '#download' },
    secondaryAction: { label: 'View on GitHub', href: repository },
    trust: ['Local-first', 'No account', 'No telemetry', 'MIT licensed'],
  },
  features: {
    title: 'Features',
    description: 'A simple and focused workflow to keep only the files you need.',
    cards: [
      { title: 'Scan', description: 'Scan folders recursively and discover file extensions, grouped with their file counts.' },
      { title: 'Filter', description: 'Find and select the file extensions you need, keeping unwanted file types out of your archive.' },
      { title: 'Archive', description: 'Create ZIP archives while preserving the original folder structure or flattening files into a single directory.' },
    ],
  },
  howItWorks: {
    title: 'How it works',
    description: 'Create a ZIP archive in three simple steps.',
    steps: [
      { number: '01', title: 'Choose a folder', description: 'Select the folder you want to analyze. ExtSieve scans its contents and groups files by extension.' },
      { number: '02', title: 'Select extensions', description: 'Browse the results, filter extensions, and select the file types you want to include in your archive.' },
      { number: '03', title: 'Create your ZIP', description: 'Choose whether to preserve the folder structure or flatten it, then save your ZIP archive.' },
    ],
  },
  screenshots: {
    title: 'Screenshots',
    light: 'Light',
    dark: 'Dark',
    lightAlt: 'ExtSieve in Light theme, with .cs and .md selected for a ZIP archive.',
    darkAlt: 'The same ExtSieve folder and selection in Dark theme.',
  },
  theme: {
    toDark: 'Switch to dark mode', toLight: 'Switch to light mode',
  },
  menu: 'Menu',
  gallery: {
    open: 'View full-size screenshot', close: 'Close image', original: 'Open image file',
    zoom: 'Actual size', viewerLabel: 'Screenshot image',
  },
  download: {
    title: 'Download ExtSieve',
    description: 'Get the latest official release for Windows or Linux.',
    action: { label: 'View all releases on GitHub', href: `${repository}/releases` },
    requirements: { label: 'Linux requirements', href: `${repository}/blob/main/LINUX_REQUIREMENTS.md` },
    unavailable: 'Download information is temporarily unavailable. Visit GitHub Releases for official packages.',
    noStable: 'No stable release is available yet. Check GitHub Releases for updates.',
    packages: {
      'windows-installer': 'Installer .exe',
      'windows-portable': 'Portable .zip',
      'linux-deb': 'Debian package .deb',
      'linux-rpm': 'RPM package .rpm',
      'linux-portable': 'Portable archive .tar.gz',
    },
    platforms: [
      { family: 'windows', name: 'Windows', architecture: 'x64' },
      { family: 'linux', name: 'Linux', architecture: 'x64' },
    ],
  },
  footer: {
    description: 'Free, local, and open source.',
    links: [
      { label: 'GitHub', href: repository },
      { label: 'Releases', href: `${repository}/releases` },
      { label: 'Documentation', href: `${repository}/blob/main/README.md` },
      { label: 'License', href: `${repository}/blob/main/LICENSE` },
    ],
    license: 'MIT License',
  },
} as const satisfies SiteContent;
