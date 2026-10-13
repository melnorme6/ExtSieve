import { spawn } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { prepareScreenshots } from './screenshot-tools.mjs';
import { validateOutput } from './quality-gate.mjs';
import { readSiteSettings } from '../src/lib/site-settings.ts';

const args = process.argv.slice(2);
if (args[0] === 'preview' && process.env.SITE_PREVIEW_OUTPUT) {
  if (args.includes('--outDir')) throw new Error('Conflicting preview output paths.');
  args.push('--outDir', process.env.SITE_PREVIEW_OUTPUT);
}
const modeIndex = args.indexOf('--site-mode');
if (modeIndex !== -1) {
  const selected = args[modeIndex + 1];
  if (!selected || (process.env.SITE_BUILD_MODE && process.env.SITE_BUILD_MODE !== selected)) {
    throw new Error('Missing or conflicting explicit site mode.');
  }
  process.env.SITE_BUILD_MODE = selected;
  args.splice(modeIndex, 2);
}
const settings = readSiteSettings(process.env);
process.env.SITE_BUILD_MODE = settings.mode;

if (['dev', 'check', 'build'].includes(process.argv[2]) && !process.argv.includes('stop')) {
  await prepareScreenshots();
}

const packageRoot = new URL('../node_modules/astro/', import.meta.url);
const { bin } = JSON.parse(readFileSync(new URL('package.json', packageRoot), 'utf8'));
const cli = fileURLToPath(new URL(typeof bin === 'string' ? bin : bin.astro, packageRoot));
const child = spawn(process.execPath, [cli, ...args], {
  stdio: 'inherit',
  env: { ...process.env, ASTRO_TELEMETRY_DISABLED: '1' },
});

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => child.kill(signal));
}

child.on('error', (error) => {
  console.error(error.message);
  process.exitCode = 1;
});

child.on('exit', async (code, signal) => {
  process.exitCode = code ?? (signal ? 1 : 0);
  if (code === 0 && args[0] === 'build') {
    try {
      const outIndex = args.indexOf('--outDir');
      const output = outIndex === -1 ? new URL('../dist/', import.meta.url) : args[outIndex + 1];
      const result = await validateOutput(output instanceof URL ? fileURLToPath(output) : output, settings.mode, settings.verification);
      console.info('[quality-gate] PASS', JSON.stringify(result));
    } catch (error) {
      console.error('[quality-gate] FAIL', error.message);
      process.exitCode = 1;
    }
  }
});
