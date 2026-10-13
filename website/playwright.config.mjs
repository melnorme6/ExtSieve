import { defineConfig } from '@playwright/test';

const browserName = process.env.PLAYWRIGHT_BROWSER || 'chromium';
if (!['chromium', 'firefox', 'webkit'].includes(browserName)) throw new Error('Unknown browser selection.');

export default defineConfig({
  testDir: './tests/browser',
  outputDir: './test-results/browser',
  fullyParallel: false,
  workers: 1,
  timeout: 30000,
  reporter: 'list',
  use: {
    baseURL: 'http://127.0.0.1:4335/ExtSieve/',
    browserName,
    channel: browserName === 'chromium' ? (process.env.PLAYWRIGHT_CHANNEL === 'chromium' ? undefined : (process.env.PLAYWRIGHT_CHANNEL || 'chrome')) : undefined,
    headless: true,
  },
  webServer: {
    command: 'node scripts/astro.mjs preview --host 127.0.0.1 --port 4335',
    url: 'http://127.0.0.1:4335/ExtSieve/',
    reuseExistingServer: false,
    timeout: 30000,
  },
});
