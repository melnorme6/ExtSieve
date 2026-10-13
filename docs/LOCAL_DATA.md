# Local data and recovery

ExtSieve 1.1.0 keeps three independent JSON files in the platform-provided per-user
local application-data `ExtSieve` directory. It creates no metadata in source folders,
requires no account, and transmits no data.

| File | Stored values |
| --- | --- |
| `settings.json` | Language and theme enums |
| `profiles.json` | Schema version 1; profile names and extension keys |
| `exclusions.json` | Schema version 1; hashed source-root keys and relative directory paths |

Profiles contain no filenames, file paths, counts, byte totals, or scan manifests.
The empty extension key `""` represents the extensionless group in every language.
Profile names are trimmed, limited to 80 characters without control characters, and
unique using ordinal case-insensitive comparison. Extension keys are normalized and
sorted ordinally. Profiles can be empty; the extensionless key counts as one entry.
Duplicate names are rejected with an explanation rather than overwriting another
profile. Rename preserves keys. Duplication suggests the first available localized
copy name, adding 2, 3, and later suffixes when needed and shortening a long source
name within the same limit. The suggested name remains editable. New, Rename, and
Duplicate show compact name-only dialogs; main Save profile retains its key preview.

Profile management creates a named empty profile independently of the current scan.
Each extension appears as a removable chip; the final plus opens a small extension
input dialog. Input such as `CS` or `.cs` becomes `.cs` through the shared profile-key
normalizer. Custom keys need not occur in the current scan. Use the explicit no-extension
option for `""`; blank ordinary input and duplicates are rejected. Removing the last
key leaves an empty profile. Changes become visible only after a successful atomic
save. Main-window Save profile still captures a non-empty current selection.

## Portable profile files

Import and export use UTF-8 JSON with this versioned format:

```json
{
  "schemaVersion": 1,
  "profiles": [
    { "name": "Unity", "extensions": [".asset", ".cs"] }
  ]
}
```

Export contains only the selected profile, including an empty one. Import accepts
multiple profiles in one file, validates the complete document before editing local
storage, and skips names already present using the same case-insensitive comparison.
The dialog reports the imported and skipped counts. Duplicate names within the file,
invalid names/keys, unsupported schemas, unknown properties, malformed JSON, and input
above 4 MiB are rejected. No source paths, filenames, exclusions, scan totals, or app
preferences are included. This portable format is distinct from the local store's
`SchemaVersion`/`Profiles`/`Name`/`ExtensionKeys` field names. Import is committed in one
atomic local save; export uses a temporary sibling and replacement after flushing.
Cancelled/failed export preserves the existing destination. Pick a local JSON file
using the native file dialog.

## Application and exclusions

Choosing a profile in the main picker replaces the selection across the entire scan
result. Missing extension groups are ignored. A profile with no matching groups clears
the selection. New scans clear selection and the profile picker; no profile is reapplied
to future scans. The main-toolbar **Deselect profile** X clears only the picker without
confirmation, persistence changes, or changes to the current extension selection.
Deleting a saved profile is available in profile management, requires confirmation,
and preserves the current extension selection.

Exclusion paths use `/` separators and name exact relative directories. Backslashes
are accepted as separators; trailing separators are removed. Absolute paths,
traversal segments, empty segments, wildcards, and patterns are rejected. Comparison
is ordinal case-insensitive on Windows and ordinal on Linux. Excluding a parent
subsumes its children; unchecking it includes that subtree again. Missing directories
are harmless and retained for future scans of the same source. No globs, regular
expressions, presets, or `.gitignore` rules are inferred.

The source-root key is lowercase SHA-256 of UTF-8 `windows:` plus the full normalized
path in invariant uppercase on Windows, or `unix:` plus the case-preserving full path
on Linux. A trailing root separator is removed where it is not the volume root.
Renaming or moving a source changes its association key. The hash is a local lookup
key, not encryption or an anonymity guarantee. Absolute source and destination paths,
archive history, and scan data are not persisted.

The exclusion dialog keeps a separate draft. Cancel leaves the scan and saved data
unchanged. Reset restores the opening draft. Manage rules through tree checkboxes.
Show only excluded is a visual filter over all configured relative paths, including
unloaded or missing directories, and combines with the last completed search query.
Full relative labels provide context. Unchecking removes the rule; disabling the filter
restores the normal browsed/search tree. Clearing search preserves the filter, and
the localized counter always describes the whole draft. Apply with a
changed draft invalidates the old manifest, saves the configuration, and rescans.
If writing fails, the current exclusions still apply in this session and an explicit
warning explains that they will not be remembered. Refresh keeps that session draft;
changing source loads the other source's saved configuration.

Directory browsing is lazy and skips links. Search is explicit, directory-only, and
cancellable; it does not descend into already excluded subtrees. At 10,000 visited
directories or 500 matches it stops and explains how to refine the search. These are
dialog-search limits, not limits on file scanning or archive size.

Writes use a unique temporary sibling, flush the serialized data, and rename over
the final file after completion. Cancelled/failed writes preserve previous data.
Writes above 4 MiB are rejected before replacing the previous file.
Missing storage starts empty. Reads reject malformed JSON, invalid content,
unsupported schema versions, and files above 4 MiB. Original bytes are preserved.
Profiles then become read-only with a warning; repair the file and restart to edit
them. An exclusion load failure visibly applies no exclusions; review the warning
and resulting dataset before saving an archive. A failed save does not silently
replace invalid storage. Back up a damaged local-data file before repairing it.

Normal uninstall preserves all per-user data and user-created archives. To reset a
library deliberately, close ExtSieve and move its JSON file to a backup location.
The next launch starts that library empty. Language and theme remain independent.
