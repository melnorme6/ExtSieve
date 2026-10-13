# ExtSieve Website

Static Astro source for <https://melnorme6.github.io/ExtSieve/>. The homepage includes
authentic application screenshots, a Sun/Moon toggle and an accessible
image viewer. Download rows use validated stable GitHub Release data obtained during
the build. Ordinary builds are private previews with noindex; an explicitly selected
release candidate is indexable. No project command deploys the site.

## Requirements and commands

Use Node 24.18.0 (`.nvmrc`) and npm 11.16.0 (`packageManager`). From this directory:

```sh
npm ci
npm run verify
npm run dev
```

On Windows PowerShell, use `npm.cmd` if the npm.ps1 wrapper is restricted. No execution
policy change is needed. `npm run build` includes strict Astro/type diagnostics;
`npm test` verifies the built page and assets, so build before running tests alone.
`npm run preview -- --host 127.0.0.1` serves the production output for local checks.
Open its `/ExtSieve/` path, including the trailing slash.

Stop your dev server before `npm ci`: Windows can lock loaded native tool modules.

Direct dependencies are pinned, the lockfile records integrity, and `.npmrc` disables
dependency lifecycle scripts. The Node CLI wrapper disables Astro development
telemetry for dev/check/build/preview. No server adapter or browser framework is used.

## Local development and preview

`npm run dev` starts Astro with automatic updates after source edits. Open the URL
printed in its terminal, normally <http://localhost:4321/ExtSieve/>. Keep the project
subpath and trailing slash; use the printed port if the default is occupied.

Stop development with Ctrl+C before checking the production output:

```sh
npm run build
npm test
npm run preview
```

Preview serves the last `dist/` build without source hot reload. Rebuild after edits.
Both commands use the same `/ExtSieve/` base; preview is a local check, not deployment.
`npm run check` runs diagnostics alone; `npm run verify` combines build and tests.

In VS Code, use **Browser: Open Integrated Browser** from the Command Palette if
available, then paste Astro's URL. Otherwise open the URL directly in Edge or Chrome.
Use browser developer tools to inspect Console and Network and emulate narrow
viewports. [VS Code browser documentation](https://code.visualstudio.com/docs/debugtest/integrated-browser)
describes availability and controls. No browser extension is required to run Astro.

## Structure

- `src/content/en.ts`: typed English copy, separate from presentation.
- `src/components/`: structural Header, Hero, Features, HowItWorks, Screenshots,
  Download and Footer components.
- `src/layouts/`, `src/pages/`: shared document and static route composition.
- `src/styles/`: semantic light/dark tokens and custom responsive CSS.
- `src/lib/paths.ts`: base-aware public asset URLs.
- `src/lib/releases.ts`: shared build-time release provider and validation.
- `src/lib/download-presentation.ts`: package labels and download presentation.
- `public/fonts/`, `public/icons/`: local licensed font and approved product mark.
- `src/assets/icons/tabler/`: pinned local outline SVG subset, rendered inline at build time.
- `public/licenses/`: complete notices for the local icon assets.
- `src/lib/screenshot-config.ts`: source identities and one set of detail rectangles.
- `src/scripts/`: early theme preference and small browser interactions.
- `scripts/screenshot-tools.mjs`: lossless detail preparation using Astro's image engine.
- `tests/`: Node checks of built output, crop pixels, source identities and theme bootstrap.

The configuration separates `site` from `base: '/ExtSieve/'`. Without a saved manual
choice, the site follows the system preference and reacts to changes. The header
toggle shows the next action: Moon switches Light to Dark; Sun switches Dark to Light.
Its first activation saves a manual Light/Dark preference for later visits. A previous
System setting is treated as implicit System. Storage errors leave the toggle usable
for the current page. There is no theme menu or visible System reset control.
CSS follows the system preference with JavaScript disabled. Image links still open
their files without scripting. No package version is inferred from the website project.

## Release data

The static route consumes one shared release promise from the public API for
`melnorme6/ExtSieve`. No token is used and visitors make no GitHub API requests.
Stable tags use `vMAJOR.MINOR.PATCH`; five uploaded, positive-size application
packages and `SHA256SUMS` are required. Exact official metadata URLs, repository,
tag, names, unique IDs, publication and checksum contents are validated. Source
archives are excluded. Linux requirements are verified and linked at the release tag.
Only the small checksum file is downloaded: application binaries are never fetched
during the build. API digest/checksum consistency does not prove local binary bytes.

Requests have an 8-second timeout, bounded response sizes and at most two attempts.
Ordinary builds warn and show only general release navigation during temporary
network, rate-limit or service failures. No stable release has a distinct message.
Malformed, contradictory or incomplete release data stops every build.

`loadRelease` and `createReleaseLoader` accept explicit typed build contexts.
`initial-publication` forbids fallback and requires a complete stable release.
`release-published` additionally requires an exact event ID/tag, a match with the
current latest stable release and a minimum previously deployed version. Private
previews use `ordinary`; indexable candidates use `initial-publication`. Deployment
callers must independently validate
event provenance, trusted source revision and deployment ordering; build inputs
alone are not authorization. Unit fixtures exercise these modes without remote writes.

## Continuous integration and Pages

The dedicated [website workflow](../.github/workflows/website.yml) checks website
changes on main and pull requests, published releases and manual retries. Desktop-only
pushes do not start it. PRs, including dependency updates, run private-preview and
indexable candidate checks with read-only permissions; they never upload or deploy
a Pages artifact. An unavailable stable release blocks candidate checks.

`npm run test:modes` exercises complete/fallback/absent/invalid release fixtures.
For the lockfile-matched browser locally or in CI:

```sh
npx --no-install playwright install chromium
PLAYWRIGHT_CHANNEL=chromium npm run test:browser
```

On Linux CI, installation also uses `--with-deps`. Windows PowerShell can set
`$env:PLAYWRIGHT_CHANNEL = 'chromium'` before `npm.cmd run test:browser`.
The default local browser remains installed Chrome. Each browser job installs once.

Publishing is disabled unless a repository administrator explicitly sets
`WEBSITE_DEPLOY_POLICY` to `initial` or `enabled`. `initial` permits a missing previous
site only for the first complete-release deployment. After verifying that deployment,
switch to `enabled`; it requires a valid existing `build-provenance.json`. Unset or
`disabled` allows checks only. These settings must accompany protected main, controlled
stable tags, an Actions publishing source, restricted Actions/token policies and
review protection on the `github-pages` environment. Configuration is a prerequisite;
the workflow file alone does not activate those administrative protections.

Production builds resolve the current protected main SHA, compare the event workflow
definition with main, bind the candidate to the complete current stable release and
reject obsolete events, release/source rollback and older workflow run numbers.
The static artifact includes public source/workflow SHA, release identity/fingerprint,
run identity and a digest of its sorted file inventory. Official packages are checked
with HEAD requests; application binaries are not downloaded or rebuilt.

Only the deploy job receives Pages/OIDC write permissions. After environment review,
it rechecks main, workflow, release and the deployed floor under a shared deployment
lock, then publishes the exact same-run artifact without installing npm packages or
rebuilding. Production runs serialize without cancelling an active Pages request;
PRs cancel superseded checks. Pending-run order is not deployment freshness evidence.
API/rate-limit/provenance failures preserve the existing site. A manual retry must
select main and supply the exact current stable `release_tag`; it bypasses no gate.
An older release workflow that differs from main is refused and needs a main retry.

Weekly [Dependabot](../.github/dependabot.yml) checks npm `/website` and Actions `/`,
groups compatible minor/patch updates and opens reviewable PRs. There is no auto-merge.

## Screenshot maintenance

Preserve the three originals in `src/assets/screenshots/`. Review their dimensions,
privacy and app state before updating their hashes and rectangles in
`src/lib/screenshot-config.ts`. Coordinates are source pixels, measured from the
top-left. `dev`, `check` and `build` automatically prepare deterministic PNG and lossless WebP details
in ignored `public/screenshots/details/`, using the Sharp version already locked by
Astro. No extra dependency or external image service is required.

After editing coordinates during an active dev session, run `npm run check` or restart
dev to regenerate details. Original screenshot bytes remain unchanged; the full
Light/Dark sources are emitted once each and reused, with lossless WebP display
alternatives. Every encoding preserves exact source pixels without resizing. A
responsive picture selects the shared phone hero framing; larger screens show the
complete window. Each step displays clearly separated authentic control crops;
the phone archive step shows both layout choices, Output and Save as individual
details. The card images fit their panels without horizontal scrolling.
Each step has one clickable image area and one expansion icon shared by all its
separate crops. Tap or click that area to use the existing viewer and
choose **Actual size**; **Open image file** exposes the original-resolution image.
CSS framing itself does not reduce delivered bytes. Derived details are smaller
than their sources. The full originals and wide step context remain available in
the viewer. Preserve dimensions on picture sources and images to reserve layout.

`node_modules/`, `dist/`, `.astro/` and test/cache output are generated and ignored.
Keep application build/packaging independent of this project. For asset provenance
and licenses, see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## SEO, indexing and verification

`npm run build` defaults to `private-preview`, prints its indexing decision and
includes `noindex, nofollow` plus the preview notice. `npm run build:candidate`
explicitly selects `release-candidate`: it requires complete live stable release
data, omits those private markers, and checks the final output. `SITE_BUILD_MODE`
can select either mode explicitly; unknown/empty values and conflicts fail the
build. A candidate build creates files only and grants no deployment authorization.

The blocking output gate checks the canonical, exact title/description, semantic
headings, homepage-only sitemap, local assets/anchors, Open Graph, JSON-LD, all five
official downloads, indexing mode and CSP hashes/order. Version/download JSON-LD
comes from the same normalized release as the visible rows. Unavailable release
fields are omitted in private fallback. No ratings, reviews or counts are invented.

The sitemap is `/ExtSieve/sitemap.xml`; only the origin-root `/robots.txt` can define
the host crawling policy. This project does not supply a misleading subpath file.
Configure the Search Console URL-prefix property for the canonical URL. Set optional
`GOOGLE_SITE_VERIFICATION` to the actual HTML-tag value before building; without it
the tag is absent. Do not commit the value or add Google scripts. Property verification
and sitemap submission take place after the site is published.

The local 1200 x 630 social PNG combines the unchanged official mark and approved
headline. It contains no reconstructed application UI. Font/icon notices ship intact.
All visitor resources are local; only a manual Light/Dark preference is persisted.

Astro's native CSP hashes are retained and its meta is moved immediately after the
charset, ahead of scripts/styles. The policy permits local scripts, styles, images
and fonts, authorizes exact inline script hashes and blocks connections, objects,
frames, workers, forms and inline attributes. No unsafe-inline or unsafe-eval is
allowed. A meta policy cannot provide frame-ancestors, sandbox or reporting directives;
those HTTP-header protections are not claimed on static hosting. Dev behavior is not
evidence of production CSP: test the built preview.

## Quality checks

`npm run test:modes` builds isolated, clearly labeled synthetic private fixtures and
proves strict candidate failures without calling GitHub. `SITE_TEST_FIXTURE` is
forbidden in candidate mode. Generated fixture payloads are measured and removed;
small results remain in ignored `test-results/`. Ordinary unit tests exercise release
validation and output mutations without a live API. The candidate live build is
the separate official-data check; it never downloads complete packages.

Run `npm run test:browser` after building. Playwright serves the static output on a
dedicated loopback port and tests eight widths in both themes, axe WCAG rules, local
requests, keyboard/touch, persistence, mobile focus, lightbox, reduced motion, reflow
and actual CSP enforcement. Install Chrome, or use `npx playwright install chromium`
and set `PLAYWRIGHT_CHANNEL=chromium` for a managed browser. Select
`PLAYWRIGHT_BROWSER=chromium`, `firefox`, or `webkit` when installed;
the default remains Chromium with local Chrome. `PLAYWRIGHT_BROWSERS_PATH` can point
to an isolated browser directory. Font tests cover six-language Latin delivery and
the complete Inter fallback; hero checks cover eager theme images and all image
openers. Browser binaries are development tools and do not ship.
`SITE_PREVIEW_OUTPUT` optionally selects an
isolated built directory. The default tests require no trackers or external services.

Blocking build/unit/browser failures must be resolved before publication. Lighthouse
scores are informational measurements, not per-commit thresholds. Physical 200%/400%
zoom, screen-reader review, final visual approval and live-host checks remain manual;
automated tests do not certify WCAG compliance.
