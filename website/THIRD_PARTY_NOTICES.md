# Website Third-party Notices

## Inter

Inter 4.1 is licensed under the SIL Open Font License 1.1 (`OFL-1.1`). The unchanged
variable WOFF2 is preserved locally as the complete fallback. Its complete upstream license and copyright notice
are retained in [public/fonts/OFL.txt](public/fonts/OFL.txt) and included in builds.

Source commit: `e3a3d4c57d5ecc01453a575621882a384c1995a3` in
[rsms/inter](https://github.com/rsms/inter/releases/tag/v4.1).
Original path: `docs/font-files/InterVariable.woff2`. The original is unchanged.

The default `InterVariable-latin.woff2` is a local subset of that exact source,
prepared by [scripts/subset-font.py](scripts/subset-font.py) using FontTools 4.66.1
and Brotli 1.2.0 (both MIT, optional asset-maintenance tools). Ordinary builds need
neither Python nor those tools. The subset retains both weight and optical-size
axes, original glyph outlines/metrics, standard text shaping and tabular numbers.
Unused optional stylistic sets and fraction features remain in the full original.
Its Unicode
range covers English, Italian, German, French, Spanish and Brazilian Portuguese,
including combining accents and common punctuation. CSS selects the unchanged
complete Inter for other characters; only the Latin asset is preloaded.
The full OFL copyright/permission/disclaimer remains bundled. The upstream license
does not declare a Reserved Font Name. No typeface substitution or external font
service is used. Rebuild with the pinned tools and verify the exact digest below.

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| `public/fonts/InterVariable.woff2` | 352,240 | `693b77d4f32ee9b8bfc995589b5fad5e99adf2832738661f5402f9978429a8e3` |
| `public/fonts/InterVariable-latin.woff2` | 94,632 | `42722df2a06cd8a27d46a11abec05d40c65d06aeab8fa99ea205179acca7e270` |
| `public/fonts/OFL.txt` | 4,380 | `262481e844521b326f5ecd053e59b98c8b2da78c8ee1bdbb6e8174305e54935a` |

## Tabler Icons

Eighteen unmodified outline SVGs from Tabler Icons v3.49.0 are imported locally and
inlined by the static build. No icon package, font, CDN or runtime library is used.
Source commit: `bbed884d15354b5cebf2493371f20dc2d5e83eaf` in
[tabler/tabler-icons](https://github.com/tabler/tabler-icons/tree/bbed884d15354b5cebf2493371f20dc2d5e83eaf).

Tabler Icons uses MIT, Copyright (c) 2020-2026 Paweł Kuna. Its unchanged complete
license is retained in [public/licenses/tabler-icons-LICENSE.txt](public/licenses/tabler-icons-LICENSE.txt)
and included in every build. License SHA-256:
`b740a1d46122672da62833e97f7e7c8a13fa85cbc7445b584b297cc00dde93db`.

The subset in src/assets/icons/tabler is scan, filter, archive, sun, moon,
arrows-maximize, menu-2, arrow-right, brand-windows, terminal-2, x, zoom-in and
external-link, device-desktop, user-off, chart-bar-off, license and download. terminal-2
represents the Linux platform without claiming a particular
distribution's branding. Third-party brand icons do not grant trademark rights.

## Product mark

`public/icons/extsieve.png` is an exact copy of the approved product's
`branding/app/extsieve-app-icon-256.png`, documented in its branding inventory.
SHA-256: `25f012127579f7a0c045ba3cdaf3e1e91dd25075322230eb27c6932c428e8af0`.
No concept mockups or application screenshot substitutes are included.

The local social card in public/social/extsieve-og.png combines this unchanged mark,
the product name and the approved headline on a simple background. It is 1200 x 630;
SHA-256: `43380b5a6d10c12e4cb99f72b768875a69524cf57d53e248dd70b82658c1346e`.
No application UI content is added or reconstructed.

## Development tools

Astro 7.3.8 and @astrojs/check 0.9.10 use MIT; TypeScript 6.0.3 uses Apache-2.0.
They compile/check the site and are not shipped as browser or desktop dependencies.
Playwright 1.64.0 uses Apache-2.0; @axe-core/playwright 4.13.0 and axe-core use
MPL-2.0. These unmodified development tools test the static output and are excluded
from the website and desktop payload. Their upstream license files remain installed.
The committed package-lock records exact transitive versions and registry integrity;
installed packages contain their own license/notice files. Review those licenses
when changing the lockfile. Do not distribute node_modules or package caches.

Transitive build tools include Lightning CSS 1.33.0 (`MPL-2.0`) and Sharp 0.35.5
(`Apache-2.0`) with native libvips components (`LGPL-3.0-or-later`). They remain
unmodified development tools with their installed upstream notices. The static
output contains no copies of these tool libraries or binaries. Their licenses must
be reassessed before modifying or redistributing the tools themselves.
