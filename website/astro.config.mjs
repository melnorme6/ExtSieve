import { defineConfig } from 'astro/config';
import { publicOrigin, publicBase, readSiteSettings } from './src/lib/site-settings.ts';
import { earlyCsp } from './scripts/csp-order.mjs';

const settings = readSiteSettings(process.env);
console.info(`[site-mode] ${settings.mode}; indexing ${settings.indexable ? 'enabled' : 'disabled'}; no deployment`);

export default defineConfig({
  site: publicOrigin,
  base: publicBase,
  output: 'static',
  trailingSlash: 'always',
  devToolbar: { enabled: false },
  build: { inlineStylesheets: 'never' },
  // Preview is same-origin; CORS Vary: Origin makes WebKit refetch font preloads.
  vite: { build: { assetsInlineLimit: 0 }, preview: { cors: false } },
  markdown: { syntaxHighlight: false },
  integrations: [earlyCsp()],
  security: {
    csp: {
      algorithm: 'SHA-256',
      directives: ["default-src 'none'", "base-uri 'none'", "object-src 'none'",
        "connect-src 'none'", "font-src 'self'", "img-src 'self'", "form-action 'none'",
        "frame-src 'none'", "media-src 'none'", "worker-src 'none'"],
      scriptDirective: { resources: [{ resource: "'self'", kind: 'element' }, { resource: "'none'", kind: 'attribute' }] },
      styleDirective: { resources: [{ resource: "'self'", kind: 'element' }, { resource: "'none'", kind: 'attribute' }] },
    },
  },
});
