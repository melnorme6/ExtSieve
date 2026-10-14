# Security Policy

## Supported versions

| Version | Supported |
| --- | --- |
| 1.1.x | Supported |
| 1.0.x | No longer supported |

## Local data

The application performs no background network requests and never writes metadata to
the selected source directory. Local profiles store only a user-chosen name and
normalized extension keys. Exclusions store exact relative directory paths and a
SHA-256 association key derived from the normalized source root. This hash minimizes
path persistence; it does not provide encryption or prevent guessing a known path.
Scan manifests and absolute source/output paths remain in memory only.

Excluded directories are pruned before scanning their contents. Links/reparse points
are skipped, and archive creation revalidates the source files from the resulting
manifest. Corrupt or unsupported local-data files are preserved rather than silently
overwritten. See [local data and recovery](docs/LOCAL_DATA.md).

## Manual update checks and external links

The application never checks for updates at startup, when opening Settings or changing
pages, through timers, or in the background. Only selecting **Check for updates** in
About requests the latest stable release from the configured public GitHub repository.
The request is anonymous and sends only the application name/version as User-Agent
and normal HTTP/API headers. GitHub receives the normal connection information needed
to serve HTTPS; no source/destination paths, exclusions, profiles, extension keys,
filenames, machine identifiers, persistent identifiers, telemetry, or analytics are sent.
No tokens or credentials are used, cookies and automatic redirects are disabled, and
timeout, rate-limit, offline, or invalid responses produce a retryable localized message.

Selecting **Open GitHub Releases**, **Open release**, or the explicitly embedded
public commit opens the default browser.
These actions do not run on their own. A detected update offers only its public release
page; ExtSieve never downloads packages, installs updates, replaces executables, or
starts an update helper. Build provenance is read from embedded assembly metadata,
without a runtime Git dependency or dynamic build timestamp. About never derives a
public commit from a private development revision. Without explicit public provenance,
it shows **Development build**; public build/publish requires a valid public revision.

## Reporting a vulnerability

Do not disclose a suspected vulnerability in a public issue.

Use the hosting platform's private vulnerability reporting feature. If that feature is
unavailable, email the project role address at
`extsieve@outlook.com` with the subject `ExtSieve security report`. Do not disclose a
suspected vulnerability in a public issue or discussion.

Include a clear description, affected version or commit, reproduction steps, expected
impact, and any suggested mitigation. Do not include unrelated private data or secrets.
