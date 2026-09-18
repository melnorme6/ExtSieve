# Linux runtime requirements

The `linux-x64` packages target 64-bit glibc-based desktop distributions. They do not
target Alpine Linux or other musl-based systems, and they do not currently target ARM64.

## Verified environments

The same `extsieve_1.0.0_amd64.deb` release package has passed the complete native-package
lifecycle on:

- Debian 12 (Bookworm) x86_64;
- Debian 13 (Trixie) x86_64;
- Ubuntu 22.04 LTS x86_64;
- Ubuntu 24.04 LTS x86_64;
- Ubuntu 26.04 LTS x86_64.

The same `extsieve-1.0.0-1.x86_64.rpm` release package has passed the complete
native-package lifecycle on:

- Fedora 43 x86_64;
- Fedora 44 x86_64.

The portable `ExtSieve-1.0.0-linux-x64.tar.gz` payload has additionally passed its
portable checksum/manifest, extraction, graphical-launch, and archive-workflow smoke on
Ubuntu 22.04 LTS, Debian 13, and Fedora 44.

These are verified reference environments, not a claim that they are the only compatible
glibc distributions. An untested distribution should be treated as expected-compatible
only when its native ABI and required libraries are suitable; it is not a verified target
until its release lifecycle has passed.

An X11 desktop session is currently required. CI launch checks use Xvfb; a normal user
launch uses the active graphical session.

## Required native libraries

ExtSieve is self-contained for .NET, but self-contained applications still use native
operating-system libraries. The target system must provide equivalents for:

- glibc, `libgcc`, `libstdc++`, CA certificates, time-zone data, GSSAPI/Kerberos, ICU,
  and OpenSSL as required by the current .NET 10 payload;
- X11, ICE, SM, and Fontconfig as required by the current Avalonia Linux desktop backend.

The DEB declares the common Debian/Ubuntu dependencies plus validated ICU and OpenSSL
package-name alternatives so APT can resolve the package available on the tested release.
The RPM declares the corresponding Fedora package names. Neither native package depends
on an installed .NET runtime.

The current DEB dependency metadata declares:

```text
ca-certificates
libc6 (>= 2.27)
libgcc-s1
libgssapi-krb5-2
libstdc++6
libicu70 | libicu72 | libicu74 | libicu76 | libicu78
libssl3t64 | libssl3
tzdata
libx11-6
libice6
libsm6
libfontconfig1
```

The current RPM metadata declares `ca-certificates`, `glibc >= 2.27`, `libgcc`,
`libstdc++`, `krb5-libs`, `libicu`, `openssl-libs`, `tzdata`, `libX11`, `libICE`,
`libSM`, and `fontconfig`.

Package names and ABI versions vary by distribution. Release validation therefore checks
the actual payload, package-manager resolution, unresolved native libraries, and the
oldest approved environment instead of relying only on a generic framework baseline.
Avalonia currently documents glibc 2.17 as the build baseline for its supplied Skia
native library, but that component-level baseline is not a compatibility claim for the
complete ExtSieve payload.

References:

- [Avalonia supported platforms](https://docs.avaloniaui.net/docs/supported-platforms)
- [.NET on Debian](https://learn.microsoft.com/dotnet/core/install/linux-debian#dependencies)

## Native-package installation

Install the Debian package with APT or the RPM package with DNF so the package manager can
resolve native dependencies and own the installed files. Native packages install the
application under `/usr/lib/extsieve`, the launcher at `/usr/bin/extsieve`, and desktop
integration under `/usr/share`.

## Portable archive

The portable archive does not install or resolve operating-system dependencies. Ensure
the required native libraries listed above are available through the target
distribution, extract the archive, keep its files together, and run:

```bash
./extsieve
```

For users on one of the verified DEB/RPM environments, the native package is normally the
simpler option because APT or DNF resolves the declared dependencies automatically. The
portable archive is useful when a native package is not desired, but a successful launch
still depends on compatible glibc/native libraries and an X11 desktop session.

The portable archive includes `share/applications/extsieve.desktop` and
`share/icons/hicolor/256x256/apps/extsieve.png` for packagers or administrators to
install under an appropriate desktop prefix. The `extsieve` launcher must be available on
the desktop session's `PATH` for that desktop entry. The portable archive does not write
system files or install itself automatically.
