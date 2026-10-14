# ExtSieve Website

Static Astro source for the [ExtSieve website](https://melnorme6.github.io/ExtSieve/),
with application screenshots, Light/Dark themes, an image viewer and release downloads.

## Requirements

- Node.js 24.18.0 (see `.nvmrc`).
- npm 11.16.0 (see `package.json`).

## Local development

From this directory:

```sh
npm ci
npm run dev
```

Open the URL printed in the terminal, normally <http://localhost:4321/ExtSieve/>.
On Windows PowerShell, use `npm.cmd` if the npm wrapper is restricted.

## Build and tests

| Command | Purpose |
| --- | --- |
| `npm run check` | Check Astro and TypeScript source |
| `npm run build` | Check and build the site into `dist/` |
| `npm run preview` | Serve the last build locally |
| `npm test` | Run Node tests against built output |
| `npm run verify` | Build and run Node tests |
| `npm run test:browser` | Run Playwright browser tests against built output |

Build before running tests. Browser tests use installed Chrome by default; their
server starts automatically. Local commands do not deploy the site.

## Structure

- `src/`: pages, components, styles, content, browser scripts and application assets.
- `public/`: static fonts, icons and license notices.
- `scripts/`: build helpers.
- `tests/`: Node and Playwright tests.

## Licenses

The site uses the project's [MIT License](../LICENSE). See
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for dependency and asset licenses.
