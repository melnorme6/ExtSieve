# ExtSieve Website

Static Astro source for <https://melnorme6.github.io/ExtSieve/>. It includes authentic
application screenshots, a Light/Dark toggle, an accessible image viewer and
download links from GitHub Releases. Visitors use local assets and make no GitHub
API requests. Application build and packaging are separate from this project.

## Requirements and installation

Use Node 24.18.0 (`.nvmrc`) and npm 11.16.0 (`packageManager`). From this directory:

```sh
npm ci
npm run dev
```

On Windows PowerShell, use `npm.cmd` if the npm.ps1 wrapper is restricted. Stop
the development server before reinstalling dependencies because Windows can lock
loaded native modules. No execution policy change is needed.

Direct dependencies are pinned and `package-lock.json` records their integrity.
The `.npmrc` disables dependency lifecycle scripts. The Astro CLI wrapper disables
development telemetry. No server adapter or browser framework is used.

| Development dependency | Purpose |
| --- | --- |
| Astro | Static pages and image processing |
| TypeScript and @astrojs/check | Typed source and Astro diagnostics |
| Playwright | Browser interaction and layout checks |
| @axe-core/playwright | Accessibility checks in browser tests |

## Local development and commands

`npm run dev` reloads after source edits. Open the URL printed in the terminal,
normally <http://localhost:4321/ExtSieve/>. Preserve the `/ExtSieve/` subpath and
trailing slash; the port can change if another process is using the default.

| Command | Purpose |
| --- | --- |
| npm run dev | Start the development server |
| npm run check | Run strict Astro and TypeScript diagnostics |
| npm run build | Check and build the default preview output |
| npm test | Check an existing build and its assets |
| npm run verify | Build and run Node tests |
| npm run build:candidate | Build indexable output using complete stable release data |
| npm run preview | Serve the last production build locally |
| npm run test:modes | Exercise release-data and build-mode fixtures |
| npm run test:browser | Run Playwright tests against built output |

Stop development with Ctrl+C before checking a production build:

```sh
npm run verify
npm run preview -- --host 127.0.0.1
```

Preview serves `dist/` without source hot reload. Rebuild after edits and open the
printed `/ExtSieve/` URL. None of these commands deploys the site.

## Structure

- `src/content/en.ts`: English copy, separate from presentation.
- `src/components/`, `src/layouts/`, `src/pages/`: components and static routes.
- `src/styles/`: responsive styling and semantic theme tokens.
- `src/lib/`: base-aware asset paths, release data and download presentation.
- `src/scripts/`: theme preference and browser interactions.
- `src/assets/screenshots/`: authentic application screenshot originals.
- `src/lib/screenshot-config.ts`: screenshot hashes and source-pixel rectangles.
- `scripts/screenshot-tools.mjs`: deterministic screenshot detail preparation.
- `public/fonts/`, `public/icons/`, `src/assets/icons/tabler/`: local visual assets.
- `public/licenses/`: notices for bundled icon assets.
- `tests/`: Node tests; Playwright configuration selects browser checks.

The Astro configuration separates the site origin from `base: '/ExtSieve/'`.
Asset links must respect that base. The site follows the system theme until a
visitor chooses Light or Dark; only that manual preference is stored. Without
scripting, CSS follows the system theme and image links still open their files.
Storage errors leave theme selection usable for the current page.

## Release data and indexing

The build reads the public API for `melnorme6/ExtSieve` and downloads the small
`SHA256SUMS` file. A stable release provides five application packages and that
checksum file; source archives are excluded from the download rows. Application
binaries are not downloaded during the website build. Linux requirements link to
the corresponding release tag.

Default builds use `private-preview`, with `noindex, nofollow` and a preview notice.
Temporary API or rate-limit failures show general release navigation in this mode.
`npm run build:candidate` uses `release-candidate`, requires complete stable release
data and produces indexable output. `SITE_BUILD_MODE` can select either mode;
invalid values and malformed release data fail the build.

The homepage supplies canonical, Open Graph and JSON-LD metadata. Its sitemap is
`/ExtSieve/sitemap.xml`; a subpath `robots.txt` would not control host-wide crawling.
Optional `GOOGLE_SITE_VERIFICATION` adds an HTML verification tag when configured.
The built Content Security Policy permits local resources and hashed inline code;
browser tests check its production behavior.

## GitHub Actions and dependency updates

The [website workflow](../.github/workflows/website.yml) runs build, Node and browser
checks for website changes on `main`, pull requests, published releases and manual
runs. Checks use read-only repository permissions. Desktop-only pushes do not
trigger website checks.

Pages jobs are conditional on repository configuration. An unset
`WEBSITE_DEPLOY_POLICY` keeps publication disabled while checks continue to run.
The workflow contains the deployment configuration; local Astro commands only
create or serve files.

Weekly [Dependabot](../.github/dependabot.yml) checks npm dependencies in `/website`
and GitHub Actions, groups compatible minor/patch updates and opens pull requests.
There is no automatic merge.

## Screenshot maintenance

Keep the three originals in `src/assets/screenshots/`. Review application state,
dimensions and privacy before updating their hashes and rectangles in
`src/lib/screenshot-config.ts`. Coordinates are source pixels from the top-left.

`dev`, `check` and `build` prepare deterministic PNG and lossless WebP details in
ignored `public/screenshots/details/`, using Astro's locked Sharp dependency.
After editing coordinates during development, run `npm run check` or restart the
server. Preserve original pixels and image dimensions; no external image service
is needed. Responsive framing uses the same sources, and the viewer provides
**Actual size** and **Open image file** for detailed inspection.

## Browser checks and generated files

Build before running browser tests. By default, Playwright uses installed Chrome.
To use its lockfile-matched Chromium instead:

```sh
npx --no-install playwright install chromium
PLAYWRIGHT_CHANNEL=chromium npm run test:browser
```

Linux CI adds `--with-deps` to browser installation. In Windows PowerShell, set
`$env:PLAYWRIGHT_CHANNEL = 'chromium'` before `npm.cmd run test:browser`.
Tests cover responsive layouts, both themes, keyboard/touch interaction, image
viewer behavior, accessibility rules and CSP enforcement. Physical zoom and
screen-reader review complement automated checks.

`node_modules/`, `dist/`, `.astro/` and test/cache output are generated and ignored.
Bundled fonts and icons retain their notices; see
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for versions and licenses.
