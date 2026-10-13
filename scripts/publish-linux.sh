#!/usr/bin/env bash
set -euo pipefail

export DOTNET_CLI_UI_LANGUAGE=en-US
export LC_ALL=C

script_directory=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
product_directory=$(CDPATH= cd -- "$script_directory/.." && pwd)
solution_path="$product_directory/ExtSieve.sln"
project_path="$product_directory/src/ExtSieve.App/ExtSieve.App.csproj"
build_props_path="$product_directory/Directory.Build.props"

fail() {
    printf 'Error: %s\n' "$1" >&2
    exit 1
}

require_command() {
    command -v "$1" >/dev/null 2>&1 || fail "Required command is unavailable: $1"
}

normalize_path() {
    local path=$1
    if command -v cygpath >/dev/null 2>&1 && [[ $path =~ ^[A-Za-z]:\\ ]]; then
        cygpath --unix "$path"
    else
        printf '%s\n' "$path"
    fi
}

dependency_version() {
    local dependency_file=$1
    local library_name=$2
    local match
    local version

    match=$(grep -F -m 1 "\"$library_name/" "$dependency_file" || true)
    [[ -n $match ]] || fail "Published dependency is missing: $library_name"
    version=${match#*\"$library_name/}
    version=${version%%\"*}
    [[ -n $version ]] || fail "Published dependency version is empty: $library_name"
    printf '%s\n' "$version"
}

copy_package_notice() {
    local package_root=$1
    local package_id=$2
    local version=$3
    local source_name=$4
    local destination_name=$5
    local normalized_id
    local source_path

    normalized_id=$(printf '%s' "$package_id" | tr '[:upper:]' '[:lower:]')
    source_path="$package_root/$normalized_id/$version/$source_name"
    [[ -f $source_path ]] || fail \
        "Required notice is missing from $package_id $version: $source_name"
    cp -- "$source_path" "$package_directory/$destination_name"
}

public_release_build=${PUBLIC_RELEASE_BUILD:-false}
public_source_revision=${PUBLIC_SOURCE_REVISION:-}
provenance_arguments=("-p:PublicReleaseBuild=$public_release_build" "-p:PublicSourceRevision=$public_source_revision")
require_command dotnet
dotnet msbuild "$project_path" -target:ValidatePublicSourceRevision "${provenance_arguments[@]}"

for command_name in dotnet grep sed find sort sha256sum tar gzip mktemp tee realpath cmp cat; do
    require_command "$command_name"
done

version=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$build_props_path" |
    head -n 1)
[[ -n $version ]] || fail 'Directory.Build.props does not define the product Version.'

artifacts_directory=${EXTSIEVE_ARTIFACT_DIRECTORY:-"$product_directory/artifacts/releases/$version/candidate"}
work_directory=${EXTSIEVE_WORK_DIRECTORY:-}
check_directory_ancestors() {
    local ancestor=$1
    while [[ $ancestor != / ]]; do
        [[ ! -L $ancestor ]] || fail "Output ancestor is a symbolic link: $ancestor"
        [[ ! -e $ancestor || -d $ancestor ]] || fail "Output ancestor is not a directory: $ancestor"
        ancestor=$(dirname -- "$ancestor")
    done
}
artifacts_directory=$(realpath -m -s -- "$artifacts_directory")
check_directory_ancestors "$artifacts_directory"
if [[ -n $work_directory ]]; then
    work_directory=$(realpath -m -s -- "$work_directory")
    check_directory_ancestors "$work_directory"
    [[ ! -e $work_directory ]] || fail 'Work directory already exists; use a fresh task directory.'
    [[ $work_directory != "$artifacts_directory" &&
        $work_directory != "$artifacts_directory/"* &&
        $artifacts_directory != "$work_directory/"* ]] || fail \
        'Artifact and work directories must be separate and non-overlapping.'
fi
runtime_identifier=linux-x64
package_name="ExtSieve-$version-$runtime_identifier"
nfpm_config="$product_directory/packaging/linux/nfpm.yaml"
output_names=(
    "$package_name.tar.gz" "$package_name.manifest.sha256" "$package_name.tar.gz.sha256"
    "extsieve_${version}_amd64.deb" "extsieve_${version}_amd64.manifest.sha256" "extsieve_${version}_amd64.deb.sha256"
    "extsieve-${version}-1.x86_64.rpm" "extsieve-${version}-1.x86_64.manifest.sha256" "extsieve-${version}-1.x86_64.rpm.sha256"
)
for name in "${output_names[@]}"; do
    [[ ! -e $artifacts_directory/$name && ! -L $artifacts_directory/$name ]] || fail \
        "Artifact already exists and will be preserved: $name. Select a fresh artifact directory."
done
require_command nfpm
nfpm_version=$(nfpm --version 2>&1)
[[ $nfpm_version == *"2.47.0"* ]] || fail \
    "nFPM 2.47.0 is required; observed: $nfpm_version"
if [[ -z $work_directory ]]; then
    work_directory=$(mktemp -d "${TMPDIR:-/tmp}/extsieve-publish-linux.XXXXXXXX")
    check_directory_ancestors "$work_directory"
else
    mkdir -p -- "$(dirname -- "$work_directory")"
    mkdir -- "$work_directory"
fi
staging_directory="$work_directory/output"
mkdir -- "$staging_directory"
package_directory="$work_directory/$package_name"
archive_path="$staging_directory/$package_name.tar.gz"
manifest_path="$staging_directory/$package_name.manifest.sha256"
archive_checksum_path="$archive_path.sha256"
native_root="$work_directory/native-root"
deb_path="$staging_directory/extsieve_${version}_amd64.deb"
deb_manifest_path="$staging_directory/extsieve_${version}_amd64.manifest.sha256"
deb_checksum_path="$deb_path.sha256"
rpm_path="$staging_directory/extsieve-${version}-1.x86_64.rpm"
rpm_manifest_path="$staging_directory/extsieve-${version}-1.x86_64.manifest.sha256"
rpm_checksum_path="$rpm_path.sha256"
printf 'Work directory (retain until verification, then remove task-owned payload): %s\n' "$work_directory"

cd "$product_directory"
dotnet restore "$solution_path" --locked-mode "${provenance_arguments[@]}"
dotnet build "$solution_path" --configuration Release --no-restore "${provenance_arguments[@]}"
test_log=$(mktemp)
trap 'rm -f -- "$test_log"' EXIT
if ! dotnet test --solution "$solution_path" --configuration Release --no-build --no-restore "${provenance_arguments[@]}" |
    tee "$test_log"; then
    fail 'Release tests failed.'
fi
if [[ ${EXTSIEVE_REQUIRE_ZERO_SKIPS:-0} == 1 ]] &&
    ! grep -Eq 'skipped:[[:space:]]+0' "$test_log"; then
    fail 'The Linux release gate requires a zero-skipped test summary.'
fi
rm -f -- "$test_log"
trap - EXIT
dotnet publish "$project_path" \
    --configuration Release \
    --runtime "$runtime_identifier" \
    --self-contained true \
    --no-restore \
    --output "$package_directory" \
    "${provenance_arguments[@]}" \
    -p:PublishSingleFile=false \
    -p:PublishTrimmed=false \
    -p:PublishAot=false \
    -p:DebugSymbols=false \
    -p:DebugType=None

for document in LICENSE README.md CHANGELOG.md SECURITY.md THIRD_PARTY_NOTICES.md \
    LINUX_REQUIREMENTS.md; do
    cp -- "$product_directory/$document" "$package_directory/$document"
done
mkdir -- "$package_directory/docs"
cp -- "$product_directory/docs/LOCAL_DATA.md" "$package_directory/docs/LOCAL_DATA.md"

for directory in \
    "$package_directory/share" \
    "$package_directory/share/applications" \
    "$package_directory/share/icons" \
    "$package_directory/share/icons/hicolor" \
    "$package_directory/share/icons/hicolor/256x256" \
    "$package_directory/share/icons/hicolor/256x256/apps"; do
    if [[ ! -d $directory ]]; then
        mkdir -- "$directory"
    fi
done
cp -- "$product_directory/packaging/linux/extsieve" "$package_directory/extsieve"
cp -- "$product_directory/packaging/linux/extsieve.desktop" \
    "$package_directory/share/applications/extsieve.desktop"
cp -- "$product_directory/branding/app/extsieve-app-icon-256.png" \
    "$package_directory/share/icons/hicolor/256x256/apps/extsieve.png"
chmod 0755 -- "$package_directory/ExtSieve.App" "$package_directory/extsieve"

assets_path="$product_directory/src/ExtSieve.App/obj/project.assets.json"
dependencies_path="$package_directory/ExtSieve.App.deps.json"
[[ -f $assets_path ]] || fail "NuGet asset graph is missing: $assets_path"
[[ -f $dependencies_path ]] || fail "Published dependency graph is missing: $dependencies_path"

package_root=$(dotnet nuget locals global-packages --list |
    sed -n 's/^global-packages: //p' | head -n 1)
[[ -n $package_root ]] || fail 'The NuGet global package directory could not be resolved.'
package_root=$(normalize_path "$package_root")

runtime_package=Microsoft.NETCore.App.Runtime.linux-x64
runtime_library="runtimepack.$runtime_package"
runtime_version=$(dependency_version "$dependencies_path" "$runtime_library")
skia_package=SkiaSharp.NativeAssets.Linux
skia_version=$(dependency_version "$dependencies_path" "$skia_package")

copy_package_notice "$package_root" "$runtime_package" "$runtime_version" \
    LICENSE.TXT DOTNET_LICENSE.txt
copy_package_notice "$package_root" "$runtime_package" "$runtime_version" \
    THIRD-PARTY-NOTICES.TXT DOTNET_THIRD_PARTY_NOTICES.txt
copy_package_notice "$package_root" "$skia_package" "$skia_version" \
    LICENSE.txt SKIASHARP_LICENSE.txt
copy_package_notice "$package_root" "$skia_package" "$skia_version" \
    THIRD-PARTY-NOTICES.txt SKIASHARP_THIRD_PARTY_NOTICES.txt

find "$package_directory" -type f -name '*.pdb' -delete

for forbidden_extension in pdb log dmp; do
    if find "$package_directory" -type f -iname "*.$forbidden_extension" -print -quit |
        grep -q .; then
        fail "Package contains a forbidden .$forbidden_extension file."
    fi
done

[[ -f $package_directory/ExtSieve.App ]] || fail \
    "Published executable is missing: $package_directory/ExtSieve.App"
[[ -f $package_directory/extsieve ]] || fail \
    "Portable launcher is missing: $package_directory/extsieve"

while IFS= read -r first_party_file; do
    if grep -aF -q "$product_directory" "$first_party_file"; then
        fail "Package file contains the private build path: $(basename "$first_party_file")"
    fi
done < <(find "$package_directory" -maxdepth 1 -type f -name 'ExtSieve.*' -print)

native_app_directory="$native_root/usr/lib/extsieve"
native_doc_directory="$native_root/usr/share/doc/extsieve"
mkdir -p -- \
    "$native_app_directory" \
    "$native_root/usr/bin" \
    "$native_root/usr/share/applications" \
    "$native_root/usr/share/icons/hicolor/256x256/apps" \
    "$native_doc_directory"

while IFS= read -r -d '' source_path; do
    cp -a -- "$source_path" "$native_app_directory/"
done < <(find "$package_directory" -mindepth 1 -maxdepth 1 \
    ! -name extsieve \
    ! -name share \
    ! -name docs \
    ! -name README.md \
    ! -name CHANGELOG.md \
    ! -name SECURITY.md \
    ! -name LINUX_REQUIREMENTS.md \
    -print0)

cp -- "$product_directory/packaging/linux/extsieve-system" \
    "$native_root/usr/bin/extsieve"
cp -- "$product_directory/packaging/linux/extsieve.desktop" \
    "$native_root/usr/share/applications/extsieve.desktop"
cp -- "$product_directory/branding/app/extsieve-app-icon-256.png" \
    "$native_root/usr/share/icons/hicolor/256x256/apps/extsieve.png"
for document in LICENSE THIRD_PARTY_NOTICES.md README.md CHANGELOG.md \
    LINUX_REQUIREMENTS.md; do
    cp -- "$product_directory/$document" "$native_doc_directory/$document"
done
mkdir -- "$native_doc_directory/docs"
cp -- "$product_directory/docs/LOCAL_DATA.md" "$native_doc_directory/docs/LOCAL_DATA.md"
chmod 0755 -- "$native_root/usr/bin/extsieve" "$native_app_directory/ExtSieve.App"

nfpm_package_root=$native_root
if command -v cygpath >/dev/null 2>&1; then
    nfpm_package_root=$(cygpath --mixed "$native_root")
fi

EXTSIEVE_VERSION=$version EXTSIEVE_PACKAGE_ROOT=$nfpm_package_root \
    nfpm package --config "$nfpm_config" --packager deb --target "$deb_path"
EXTSIEVE_VERSION=$version EXTSIEVE_PACKAGE_ROOT=$nfpm_package_root \
    nfpm package --config "$nfpm_config" --packager rpm --target "$rpm_path"
[[ -f $deb_path ]] || fail "DEB output is missing: $deb_path"
[[ -f $rpm_path ]] || fail "RPM output is missing: $rpm_path"

native_manifest="$work_directory/native-package.manifest.sha256"
: > "$native_manifest"
while IFS= read -r -d '' relative_path; do
    hash=$(sha256sum "$native_root/$relative_path")
    printf '%s  /%s\n' "${hash%% *}" "$relative_path" >> "$native_manifest"
done < <(
    cd "$native_root"
    find . -type f -printf '%P\0' | sort -z
)
cp -- "$native_manifest" "$deb_manifest_path"
cp -- "$native_manifest" "$rpm_manifest_path"
rm -f -- "$native_manifest"

deb_hash=$(sha256sum "$deb_path")
printf '%s  %s\n' "${deb_hash%% *}" "$(basename "$deb_path")" \
    > "$deb_checksum_path"
rpm_hash=$(sha256sum "$rpm_path")
printf '%s  %s\n' "${rpm_hash%% *}" "$(basename "$rpm_path")" \
    > "$rpm_checksum_path"

temporary_manifest="$manifest_path.tmp"
: > "$temporary_manifest"
while IFS= read -r -d '' relative_path; do
    hash=$(sha256sum "$package_directory/$relative_path")
    printf '%s  %s\n' "${hash%% *}" "$relative_path" >> "$temporary_manifest"
done < <(
    cd "$package_directory"
    find . -type f -printf '%P\0' | sort -z
)
mv -- "$temporary_manifest" "$manifest_path"

temporary_tar="$archive_path.tmp.tar"
trap 'rm -f -- "$temporary_tar"' EXIT
tar \
    --sort=name \
    --mtime='UTC 1970-01-01' \
    --owner=0 \
    --group=0 \
    --numeric-owner \
    --mode='a-x,a+rX,u+w' \
    --exclude="$package_name/ExtSieve.App" \
    --exclude="$package_name/extsieve" \
    -C "$work_directory" \
    -cf "$temporary_tar" \
    "$package_name"
tar \
    --append \
    --sort=name \
    --mtime='UTC 1970-01-01' \
    --owner=0 \
    --group=0 \
    --numeric-owner \
    --mode=0755 \
    -C "$work_directory" \
    -f "$temporary_tar" \
    "$package_name/ExtSieve.App" \
    "$package_name/extsieve"
gzip --no-name --stdout "$temporary_tar" > "$archive_path"
rm -f -- "$temporary_tar"
trap - EXIT

archive_hash=$(sha256sum "$archive_path")
printf '%s  %s\n' "${archive_hash%% *}" "$(basename "$archive_path")" \
    > "$archive_checksum_path"

mkdir -p -- "$artifacts_directory"
for name in "${output_names[@]}"; do
    # Exclusive creation preserves a destination that appears after preflight.
    (set -o noclobber; cat -- "$staging_directory/$name" > "$artifacts_directory/$name")
    cmp -- "$staging_directory/$name" "$artifacts_directory/$name"
done
printf 'Artifact directory: %s\n' "$artifacts_directory"
printf 'Package directory: %s\n' "$package_directory"
printf 'Package archive:   %s\n' "$archive_path"
printf 'File manifest:     %s\n' "$manifest_path"
printf 'Archive checksum:  %s\n' "$archive_checksum_path"
printf 'Debian package:    %s\n' "$deb_path"
printf 'Debian manifest:   %s\n' "$deb_manifest_path"
printf 'Debian checksum:   %s\n' "$deb_checksum_path"
printf 'RPM package:       %s\n' "$rpm_path"
printf 'RPM manifest:      %s\n' "$rpm_manifest_path"
printf 'RPM checksum:      %s\n' "$rpm_checksum_path"
