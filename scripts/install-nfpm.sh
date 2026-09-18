#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
    printf 'Usage: %s TARGET_DIRECTORY\n' "$0" >&2
    exit 2
fi

target_directory=$1
nfpm_version=2.47.0
archive_name="nfpm_${nfpm_version}_Linux_x86_64.tar.gz"
checksums_name=checksums.txt
checksums_sha256=57145efa88e31a9b471755dad60b4d58a607ea4ef869a33fae902dceaf30b98b
release_url="https://github.com/goreleaser/nfpm/releases/download/v${nfpm_version}"

for command_name in curl sha256sum tar mktemp grep install; do
    command -v "$command_name" >/dev/null 2>&1 || {
        printf 'Required command is unavailable: %s\n' "$command_name" >&2
        exit 1
    }
done

temporary_directory=$(mktemp -d)
trap 'rm -rf -- "$temporary_directory"' EXIT

curl --fail --location --silent --show-error \
    --output "$temporary_directory/$checksums_name" \
    "$release_url/$checksums_name"
curl --fail --location --silent --show-error \
    --output "$temporary_directory/$archive_name" \
    "$release_url/$archive_name"

printf '%s  %s\n' "$checksums_sha256" "$temporary_directory/$checksums_name" |
    sha256sum --check --status -
grep -E "^[0-9a-f]{64}[[:space:]]+${archive_name}$" \
    "$temporary_directory/$checksums_name" > "$temporary_directory/archive.sha256"
(
    cd "$temporary_directory"
    sha256sum --check --status archive.sha256
    tar -xzf "$archive_name" nfpm
)

mkdir -p -- "$target_directory"
install -m 0755 "$temporary_directory/nfpm" "$target_directory/nfpm"
"$target_directory/nfpm" --version | grep -F "${nfpm_version}" >/dev/null
printf 'Installed nFPM %s at %s\n' "$nfpm_version" "$target_directory/nfpm"
