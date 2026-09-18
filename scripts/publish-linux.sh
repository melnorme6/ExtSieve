#!/usr/bin/env bash
set -euo pipefail

export DOTNET_CLI_UI_LANGUAGE=en-US
export LC_ALL=C

script_directory=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
product_directory=$(CDPATH= cd -- "$script_directory/.." && pwd)
solution_path="$product_directory/ExtSieve.sln"
project_path="$product_directory/src/ExtSieve.App/ExtSieve.App.csproj"
build_props_path="$product_directory/Directory.Build.props"
artifacts_directory="$product_directory/artifacts"

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

for command_name in dotnet grep sed find sort sha256sum tar gzip mktemp tee nfpm; do
    require_command "$command_name"
done

nfpm_version=$(nfpm --version 2>&1)
[[ $nfpm_version == *"2.47.0"* ]] || fail \
    "nFPM 2.47.0 is required; observed: $nfpm_version"

version=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$build_props_path" |
    head -n 1)
[[ -n $version ]] || fail 'Directory.Build.props does not define the product Version.'

runtime_identifier=linux-x64
package_name="ExtSieve-$version-$runtime_identifier"
package_directory="$artifacts_directory/$package_name"
archive_path="$artifacts_directory/$package_name.tar.gz"
manifest_path="$artifacts_directory/$package_name.manifest.sha256"
archive_checksum_path="$archive_path.sha256"
native_root="$artifacts_directory/.native-package-root-$version"
nfpm_config="$product_directory/packaging/linux/nfpm.yaml"
deb_path="$artifacts_directory/extsieve_${version}_amd64.deb"
deb_manifest_path="$artifacts_directory/extsieve_${version}_amd64.manifest.sha256"
deb_checksum_path="$deb_path.sha256"
rpm_path="$artifacts_directory/extsieve-${version}-1.x86_64.rpm"
rpm_manifest_path="$artifacts_directory/extsieve-${version}-1.x86_64.manifest.sha256"
rpm_checksum_path="$rpm_path.sha256"

if [[ ! -d $artifacts_directory ]]; then
    mkdir -p -- "$artifacts_directory"
fi
rm -rf -- "$package_directory"
rm -rf -- "$native_root"
rm -f -- \
    "$archive_path" "$manifest_path" "$archive_checksum_path" \
    "$deb_path" "$deb_manifest_path" "$deb_checksum_path" \
    "$rpm_path" "$rpm_manifest_path" "$rpm_checksum_path"

cd "$product_directory"
dotnet restore "$solution_path" --locked-mode
dotnet build "$solution_path" --configuration Release --no-restore
test_log=$(mktemp)
trap 'rm -f -- "$test_log"' EXIT
if ! dotnet test "$solution_path" --configuration Release --no-build --no-restore |
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
    -p:PublishSingleFile=false \
    -p:PublishTrimmed=false \
    -p:PublishAot=false \
    -p:DebugSymbols=false \
    -p:DebugType=None

for document in LICENSE README.md CHANGELOG.md SECURITY.md THIRD_PARTY_NOTICES.md \
    LINUX_REQUIREMENTS.md; do
    cp -- "$product_directory/$document" "$package_directory/$document"
done

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

native_manifest="$artifacts_directory/.native-package.manifest.sha256"
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
    -C "$artifacts_directory" \
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
    -C "$artifacts_directory" \
    -f "$temporary_tar" \
    "$package_name/ExtSieve.App" \
    "$package_name/extsieve"
gzip --no-name --stdout "$temporary_tar" > "$archive_path"
rm -f -- "$temporary_tar"
trap - EXIT

archive_hash=$(sha256sum "$archive_path")
printf '%s  %s\n' "${archive_hash%% *}" "$(basename "$archive_path")" \
    > "$archive_checksum_path"

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
