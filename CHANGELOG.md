# Changelog

All notable changes to ExtSieve will be documented in this file.

The format follows Keep a Changelog, and product versions follow Semantic Versioning.

## Unreleased

No changes yet.

## 1.0.0 - 2026-09-18

### Added

- Safe recursive extension filtering and deterministic, transactional ZIP creation in
  preserve-structure or flat layout.
- Responsive Avalonia desktop workflow with cancellation, replacement confirmation,
  settings persistence, accessibility support, Light/Dark/System themes, and six UI
  languages.
- Self-contained Windows x64 installer and portable ZIP.
- Self-contained Linux x64 DEB, RPM, and portable tar.gz packages for compatible
  glibc-based desktop distributions.
- MIT licensing, bundled third-party notices, SHA-256 manifests and checksums, and
  reproducible locked dependency builds.

### Changed

- Hardened final archive promotion so an unapproved, newly appeared, or concurrently
  changed destination is preserved instead of overwritten.
- Corrected source-boundary checks for filesystem-root selections and disabled
  Avalonia build-time telemetry for repository builds.
- Restyled archive-replacement confirmation actions with the shared semantic primary
  and secondary button system while retaining a safe Cancel default.
- Updated public documentation for the 1.0.0 artifact set and the six-language release
  set.
- Removed publish-unnecessary ancillary metadata from public branding source copies
  while preserving their approved pixels exactly.
- About links now keep a transparent surface during pointer interaction and change only
  their text color while preserving keyboard focus feedback.
- Refined the public README into a product-facing landing page with compact platform,
  language, license, and build badges; concise usage and packaging guidance; a single
  Light/Dark screenshot; and a static Ko-fi support button.
- Updated the public contribution policy wording and applied the approved copyright
  identification to the MIT license.
