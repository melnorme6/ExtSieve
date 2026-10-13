# Changelog

All notable changes to ExtSieve will be documented in this file.

The format follows Keep a Changelog, and product versions follow Semantic Versioning.

## Unreleased

Target version: 1.1.0.

### Added

- About displays only the explicitly embedded public GitHub commit, shortened to
  seven characters with a public commit link. Development builds show a localized
  Development build value; public packaging rejects missing/invalid provenance.
  The GitHub Releases link remains available separately.
- Manual, on-demand checks for the latest public stable release, with SemVer
  comparison, localized progress/results, duplicate-click prevention, retryable
  failures, and a browser-only action for newer releases. No automatic checks,
  downloads, installation, credentials, or telemetry.
- Named local extension profiles with explicit application, creation from selection,
  rename, duplicate, and confirmed deletion. Application replaces the complete
  selection, ignores absent groups, and supports files without extensions.
- Per-source folder exclusions with lazy directory browsing, cancellable search,
  subtree checkboxes, an excluded-only tree view, reset, and automatic rescan on change.
- Versioned local profile and exclusion storage with temporary-sibling writes, source
  keys derived with SHA-256, and preservation of corrupt or unsupported data.
- Complete six-language labels, validation, accessibility names, and themed dialogs.
- Empty named profiles, removable extension chips, custom extension-key editing, and
  versioned portable JSON import/export with explicit feedback for skipped existing names.

### Changed

- Profile name dialogs size naturally to their content; the profile picker and Deselect
  action form a closer pair. Profile management shows extension counts in a matching
  pane footer and uses integrated removal sections on extension chips.
- Deselect all clears the folder-exclusion draft without saving until Apply; Reset
  still restores the rules present when the dialog opened.
- Compact New/Rename/Duplicate name dialogs omit redundant extension previews.
  Duplication proposes a valid localized name with collision numbering.
- Folder exclusions use the tree and a search-compatible Show only excluded filter,
  replacing the separate path list and removal panel while retaining missing-path
  editing, whole-draft singular/plural counts, reset, and Apply/Cancel behavior.
- Profile management uses a grouped icon footer, a wrapping/scrollable chip editor,
  and separate empty-profile creation and duplication. Main Save profile retains
  creation from the current selection.
- Scanner prunes excluded directories before enumeration; archive manifests and totals
  contain only included files.
- Compact profile toolbar with immediate application on user selection, a Deselect X
  that preserves saved profiles and extension selection, Save and management icons,
  vertically aligned controls, a right-aligned filter, and status-bar feedback.
- Package upgrade fixtures derive their version from central product metadata and
  verify preservation of local profile and exclusion data.

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
