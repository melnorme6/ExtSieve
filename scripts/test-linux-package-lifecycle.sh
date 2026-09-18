#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 11 ]]; then
    printf '%s\n' \
        'Usage: test-linux-package-lifecycle.sh FAMILY EXPECTED_ID EXPECTED_VERSION' \
        '  PACKAGE PACKAGE_CHECKSUM UPGRADE_PACKAGE SMOKE_ARCHIVE SMOKE_CHECKSUM' \
        '  PORTABLE_ARCHIVE PORTABLE_CHECKSUM PORTABLE_MANIFEST' >&2
    exit 2
fi

family=$1
expected_id=$2
expected_version=$3
package=$4
package_checksum=$5
upgrade_package=$6
smoke_archive=$7
smoke_checksum=$8
portable_archive=$9
portable_checksum=${10}
portable_manifest=${11}

fail() {
    printf 'Error: %s\n' "$1" >&2
    exit 1
}

require_command() {
    command -v "$1" >/dev/null 2>&1 || fail "Required command is unavailable: $1"
}

verify_sidecar() {
    local payload=$1
    local sidecar=$2
    test -f "$payload" || fail "Payload is missing: $payload"
    test -f "$sidecar" || fail "Checksum sidecar is missing: $sidecar"
    (
        cd "$(dirname "$payload")"
        sha256sum --check "$(basename "$sidecar")"
    )
}

launch_application() {
    local launcher=$1
    local launch_status

    set +e
    HOME=/root DISPLAY=:99 timeout --signal=TERM --kill-after=5s 10s "$launcher"
    launch_status=$?
    set -e
    test "$launch_status" -eq 124 ||
        fail "Application launch ended with unexpected status $launch_status: $launcher"
}

report_native_versions() {
    local elf_list
    local glibc_versions
    local glibcxx_versions
    local unresolved
    local unexpected_unresolved

    elf_list=$(mktemp)
    find /usr/lib/extsieve -maxdepth 1 -type f -print0 |
        xargs -0 file |
        awk -F: '/ELF/{print $1}' > "$elf_list"
    test -s "$elf_list" || fail 'The installed payload contains no ELF files.'

    while IFS= read -r binary; do
        unresolved=$(ldd "$binary" 2>&1 | grep -F 'not found' || true)
        if [[ -n $unresolved ]]; then
            unexpected_unresolved=$(printf '%s\n' "$unresolved" |
                grep -vF 'liblttng-ust.so.0 => not found' || true)
            if [[ $(basename "$binary") == libcoreclrtraceptprovider.so ]] &&
                [[ -z $unexpected_unresolved ]]; then
                printf 'NATIVE_OPTIONAL_UNRESOLVED=%s: liblttng-ust.so.0\n' \
                    "$(basename "$binary")"
            else
                fail "Unresolved native dependency in $binary: $unresolved"
            fi
        fi
    done < "$elf_list"

    glibc_versions=$(
        while IFS= read -r binary; do
            readelf --version-info "$binary" 2>/dev/null || true
        done < "$elf_list" |
            grep -o 'GLIBC_[0-9][0-9.]*' |
            sort -Vu || true
    )
    glibcxx_versions=$(
        while IFS= read -r binary; do
            readelf --version-info "$binary" 2>/dev/null || true
        done < "$elf_list" |
            grep -o 'GLIBCXX_[0-9][0-9.]*' |
            sort -Vu || true
    )
    rm -f -- "$elf_list"

    test -n "$glibc_versions" || fail 'No GLIBC symbol requirement was detected.'
    test -n "$glibcxx_versions" || fail 'No GLIBCXX symbol requirement was detected.'

    printf 'NATIVE_GLIBC_MAX=%s\n' "$(printf '%s\n' "$glibc_versions" | tail -n 1)"
    printf 'NATIVE_GLIBCXX_MAX=%s\n' "$(printf '%s\n' "$glibcxx_versions" | tail -n 1)"
}

run_archive_smoke() {
    local smoke_tool=$1
    local work_directory=$2

    test -x "$smoke_tool/ExtSieve.PackageSmoke" ||
        fail 'The archive smoke executable is missing or not executable.'
    test -f "$smoke_tool/ExtSieve.Core.dll" ||
        fail 'The archive smoke Core assembly is missing.'
    test -f /usr/lib/extsieve/ExtSieve.Core.dll ||
        fail 'The installed ExtSieve Core assembly is missing.'
    test "$(sha256sum "$smoke_tool/ExtSieve.Core.dll" | cut -d' ' -f1)" = \
        "$(sha256sum /usr/lib/extsieve/ExtSieve.Core.dll | cut -d' ' -f1)" ||
        fail 'The archive smoke driver does not use the installed Core assembly bytes.'

    "$smoke_tool/ExtSieve.PackageSmoke" "$work_directory"
    unzip -t "$work_directory/preserve.zip"
    unzip -t "$work_directory/flat.zip"
}

test -r /etc/os-release || fail '/etc/os-release is unavailable.'
# shellcheck disable=SC1091
source /etc/os-release
test "${ID:-}" = "$expected_id" ||
    fail "Expected distribution ID $expected_id; observed ${ID:-missing}."
test "${VERSION_ID:-}" = "$expected_version" ||
    fail "Expected distribution version $expected_version; observed ${VERSION_ID:-missing}."
printf 'ENVIRONMENT=%s %s (%s)\n' "$ID" "$VERSION_ID" "$(uname -m)"
test "$(uname -m)" = x86_64 || fail 'The lifecycle environment is not x86_64.'

for command_name in sha256sum stat find grep awk sed sort tail cut timeout; do
    require_command "$command_name"
done
verify_sidecar "$package" "$package_checksum"
package_sha=$(sha256sum "$package" | cut -d' ' -f1)
printf 'PACKAGE_SHA256=%s\n' "$package_sha"

case "$family" in
    deb)
        require_command apt-get
        require_command dpkg-deb
        control_directory=$(mktemp -d)
        dpkg-deb --control "$package" "$control_directory"
        for script_name in preinst postinst prerm postrm; do
            test ! -e "$control_directory/$script_name" ||
                fail "Unexpected DEB maintainer script: $script_name"
        done
        rm -rf -- "$control_directory"

        # Official minimal container images may exclude package documentation globally.
        # Override that image-only optimization for the package whose files are audited.
        printf '%s\n' 'path-include=/usr/share/doc/extsieve/*' > \
            /etc/dpkg/dpkg.cfg.d/zz-extsieve-validation-include-docs
        export DEBIAN_FRONTEND=noninteractive

        # APT treats a bare relative path such as artifacts/package.deb as a
        # repository package name. Normalize local DEB paths while preserving
        # the literal apt-get install lines required by the packaging contract tests.
        case "$package" in
            /*|./*|../*) ;;
            *) package="./$package" ;;
        esac
        case "$upgrade_package" in
            /*|./*|../*) ;;
            *) upgrade_package="./$upgrade_package" ;;
        esac

        apt-get update
        apt-get install --yes --no-install-recommends \
            binutils desktop-file-utils file gzip procps tar unzip xauth xvfb
        apt-get install --yes "$package"
        test "$(dpkg-query --show --showformat='${Version}' extsieve)" = 1.0.0-1
        test "$(dpkg-query --show --showformat='${Architecture}' extsieve)" = amd64
        test "$(dpkg-query --search /usr/bin/extsieve)" = \
            'extsieve: /usr/bin/extsieve'
        printf '%s\n' 'RESOLVED_DEB_NATIVE_DEPENDENCIES:'
        dpkg-query --show --showformat='${binary:Package}\t${Version}\n' |
            grep -E '^(libicu[0-9]+|libssl3(t64)?|libc6|libstdc\+\+6|libgcc-s1|libgssapi-krb5-2)(:amd64)?[[:space:]]'
        ;;
    rpm)
        require_command dnf
        require_command rpm
        test -z "$(rpm --query --package --scripts "$package")" ||
            fail 'The RPM contains an unexpected package scriptlet.'

        dnf install --assumeyes \
            binutils desktop-file-utils file gzip procps-ng tar unzip \
            xorg-x11-server-Xvfb xorg-x11-xauth
        dnf --setopt=tsflags= install --assumeyes "$package"
        test "$(rpm --query --queryformat '%{VERSION}' extsieve)" = 1.0.0
        test "$(rpm --query --queryformat '%{ARCH}' extsieve)" = x86_64
        test "$(rpm --query --file /usr/bin/extsieve)" = \
            extsieve-1.0.0-1.x86_64
        printf '%s\n' 'RESOLVED_RPM_NATIVE_DEPENDENCIES:'
        rpm --query --all --queryformat '%{NAME}\t%{VERSION}-%{RELEASE}\n' |
            grep -E '^(libicu|openssl-libs|glibc|libstdc\+\+|libgcc|krb5-libs)[[:space:]]'
        ;;
    *)
        fail "Unsupported package family: $family"
        ;;
esac

for command_name in desktop-file-validate file ldd readelf tar unzip Xvfb; do
    require_command "$command_name"
done

desktop-file-validate /usr/share/applications/extsieve.desktop
test -x /usr/bin/extsieve
test -x /usr/lib/extsieve/ExtSieve.App
test -f /usr/share/icons/hicolor/256x256/apps/extsieve.png
test -f /usr/share/doc/extsieve/LICENSE
test -f /usr/share/doc/extsieve/THIRD_PARTY_NOTICES.md
test -z "$(find \
    /usr/lib/extsieve \
    /usr/share/applications/extsieve.desktop \
    /usr/share/icons/hicolor/256x256/apps/extsieve.png \
    /usr/share/doc/extsieve \
    -perm -0002 -print -quit)" || fail 'An installed package file is world-writable.'
test "$(stat -c '%U:%G' /usr/bin/extsieve)" = root:root
test "$(stat -c '%U:%G' /usr/lib/extsieve/ExtSieve.App)" = root:root
printf 'LAUNCHER_MODE=%s\n' "$(stat -c '%a' /usr/bin/extsieve)"
printf 'APPLICATION_MODE=%s\n' "$(stat -c '%a' /usr/lib/extsieve/ExtSieve.App)"
report_native_versions

settings_directory=/root/.local/share/ExtSieve
settings_file=$settings_directory/settings.json
user_archive=/root/extsieve-user-archive.zip
mkdir -p "$settings_directory"
printf '{"theme":"Dark"}\n' > "$settings_file"
printf 'owner data\n' > "$user_archive"

smoke_directory=/tmp/extsieve-package-smoke-tool
mkdir -p "$smoke_directory"
verify_sidecar "$smoke_archive" "$smoke_checksum"
tar -xzf "$smoke_archive" -C "$smoke_directory"

Xvfb :99 -screen 0 1280x800x24 >/tmp/extsieve-xvfb.log 2>&1 &
xvfb_pid=$!
trap 'kill "$xvfb_pid" 2>/dev/null || true' EXIT
sleep 1
launch_application /usr/bin/extsieve
run_archive_smoke "$smoke_directory" /root/extsieve-package-smoke

case "$family" in
    deb)
        apt-get install --yes "$upgrade_package"
        test "$(dpkg-query --show --showformat='${Version}' extsieve)" = 1.0.1-1
        test "$(dpkg-query --show --showformat='${binary:Package}\n' extsieve | wc -l)" -eq 1
        ;;
    rpm)
        dnf --setopt=tsflags= upgrade --assumeyes "$upgrade_package"
        test "$(rpm --query --queryformat '%{VERSION}' extsieve)" = 1.0.1
        test "$(rpm --query --all --queryformat '%{NAME}\n' | grep -xc extsieve)" -eq 1
        ;;
esac
launch_application /usr/bin/extsieve

case "$family" in
    deb)
        apt-get remove --yes extsieve
        ! dpkg-query --show extsieve >/dev/null 2>&1
        ;;
    rpm)
        dnf remove --assumeyes extsieve
        ! rpm --query extsieve >/dev/null 2>&1
        ;;
esac

test ! -e /usr/bin/extsieve
test ! -e /usr/lib/extsieve
test ! -e /usr/share/applications/extsieve.desktop
test ! -e /usr/share/icons/hicolor/256x256/apps/extsieve.png
test ! -e /usr/share/doc/extsieve
test -f "$settings_file"
grep -Fq '"theme":"Dark"' "$settings_file"
test -f "$user_archive"
test -f /root/extsieve-package-smoke/preserve.zip
test -f /root/extsieve-package-smoke/flat.zip

if [[ $portable_archive != - ]]; then
    verify_sidecar "$portable_archive" "$portable_checksum"
    test -f "$portable_manifest" || fail 'The portable manifest is missing.'

    # The manifest path is received relative to the workflow workspace, but the
    # checksum verification below runs from inside the extracted portable
    # directory. Convert the manifest to an absolute path before changing cwd.
    portable_manifest_directory=$(cd "$(dirname "$portable_manifest")" && pwd -P)
    portable_manifest="$portable_manifest_directory/$(basename "$portable_manifest")"

    # DNF may remove dependencies that were installed only for ExtSieve when
    # the native RPM is removed. The portable build still requires the normal
    # documented system libraries, so restore those runtime prerequisites before
    # validating the portable payload.
    if [[ $family == rpm ]]; then
        dnf install --assumeyes             ca-certificates glibc libgcc krb5-libs libstdc++ libicu             openssl-libs tzdata libX11 libICE libSM fontconfig
    fi

    portable_root=/tmp/extsieve-portable
    mkdir -p "$portable_root"
    tar -xzf "$portable_archive" -C "$portable_root"
    mapfile -t portable_directories < <(find "$portable_root" -mindepth 1 -maxdepth 1 -type d)
    test "${#portable_directories[@]}" -eq 1 ||
        fail 'The portable archive must contain one top-level directory.'
    portable_directory=${portable_directories[0]}
    (
        cd "$portable_directory"
        sha256sum --check "$portable_manifest"
    )
    test -x "$portable_directory/ExtSieve.App"
    test -x "$portable_directory/extsieve"
    desktop-file-validate "$portable_directory/share/applications/extsieve.desktop"
    test "$(sha256sum "$smoke_directory/ExtSieve.Core.dll" | cut -d' ' -f1)" = \
        "$(sha256sum "$portable_directory/ExtSieve.Core.dll" | cut -d' ' -f1)" ||
        fail 'The portable payload Core assembly differs from the archive smoke driver.'
    launch_application "$portable_directory/extsieve"
    mkdir -p /usr/lib/extsieve
    cp -- "$portable_directory/ExtSieve.Core.dll" /usr/lib/extsieve/ExtSieve.Core.dll
    run_archive_smoke "$smoke_directory" /root/extsieve-portable-smoke
    rm -rf -- /usr/lib/extsieve
    test -f /root/extsieve-portable-smoke/preserve.zip
    test -f /root/extsieve-portable-smoke/flat.zip
    printf 'PORTABLE_SMOKE=PASS\n'
else
    printf 'PORTABLE_SMOKE=NOT_SELECTED\n'
fi

printf 'PACKAGE_LIFECYCLE=PASS\n'
