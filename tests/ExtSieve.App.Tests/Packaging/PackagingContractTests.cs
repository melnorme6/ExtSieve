using System.Xml.Linq;

namespace ExtSieve.App.Tests.Packaging;

public sealed class PackagingContractTests
{
    private static readonly string[] SupportedReadmeLanguages =
    [
        "- English",
        "- Italiano",
        "- Deutsch",
        "- Français",
        "- Español",
        "- Português (Brasil)",
    ];

    [Fact]
    public void ApplicationDeclaresTheTwoSupportedRuntimeIdentifiers()
    {
        var project = XDocument.Load(ProductPath(
            "src", "ExtSieve.App", "ExtSieve.App.csproj"));

        Assert.Equal(
            "win-x64;linux-x64",
            project.Descendants("RuntimeIdentifiers").Single().Value);
    }

    [Fact]
    public void DeterministicBuildsMapPrivateProjectPathsToANeutralRoot()
    {
        var properties = XDocument.Load(ProductPath("Directory.Build.props"));

        Assert.Equal(
            "$(MSBuildProjectDirectory)=/_/",
            properties.Descendants("PathMap").Single().Value);
    }

    [Fact]
    public void ProductBuildDisablesAvaloniaBuildTelemetry()
    {
        var targets = XDocument.Load(ProductPath("Directory.Build.targets"));
        var target = Assert.Single(targets.Descendants("Target"));

        Assert.Equal("AvaloniaStats", (string?)target.Attribute("Name"));
        Assert.Empty(target.Elements());
    }

    [Fact]
    public void ProductDeclaresTheMitLicense()
    {
        var properties = XDocument.Load(ProductPath("Directory.Build.props"));
        var license = File.ReadAllText(ProductPath("LICENSE"));

        Assert.Equal(
            "MIT",
            properties.Descendants("PackageLicenseExpression").Single().Value);
        Assert.StartsWith("MIT License", license, StringComparison.Ordinal);
        Assert.Contains(
            "Copyright (c) 2026\nGiuseppe Russo (@melnorme6, github.com/melnorme6)",
            license,
            StringComparison.Ordinal);
        Assert.Contains(
            "Permission is hereby granted, free of charge",
            license,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PublicRepositoryDeclaresTheKofiSupportPage()
    {
        var properties = XDocument.Load(ProductPath("Directory.Build.props"));
        var funding = File.ReadAllText(ProductPath(".github", "FUNDING.yml"));
        var readme = File.ReadAllText(ProductPath("README.md"));
        var supportHeading = readme.IndexOf("## Support", StringComparison.Ordinal);
        var licenseHeading = readme.IndexOf("## License", StringComparison.Ordinal);

        Assert.Equal(
            "https://ko-fi.com/melnorme",
            properties.Descendants("SupportUrl").Single().Value);
        Assert.Contains("ko_fi: melnorme", funding, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://ko-fi.com/melnorme\">", readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "src=\"https://storage.ko-fi.com/cdn/brandasset/v2/support_me_on_kofi_blue.png\"",
            readme,
            StringComparison.Ordinal);
        Assert.Contains("alt=\"Support ExtSieve on Ko-fi\"", readme,
            StringComparison.Ordinal);
        Assert.True(supportHeading >= 0 && supportHeading < licenseHeading);
    }

    [Fact]
    public void PublicReadmeUsesTheProductFacingPresentation()
    {
        var readme = File.ReadAllText(ProductPath("README.md"));

        Assert.DoesNotContain("## Status", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("Possible post-release direction", readme,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Settings and About", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("Browse beside Source", readme, StringComparison.Ordinal);
        Assert.Contains("## Screenshot", readme, StringComparison.Ordinal);
        Assert.Contains(
            "![ExtSieve main view shown in Light and Dark themes](docs/screenshots/main-light-dark.png)",
            readme,
            StringComparison.Ordinal);
        Assert.Contains("## Supported languages", readme, StringComparison.Ordinal);
        var languageHeading = readme.IndexOf("## Supported languages", StringComparison.Ordinal);
        var usageHeading = readme.IndexOf("## Basic usage", StringComparison.Ordinal);
        var languageSection = readme[languageHeading..usageHeading];
        var languageItems = languageSection.Split('\n')
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(SupportedReadmeLanguages, languageItems);
        Assert.Contains(
            "Some non-English translations were prepared with AI assistance and may be refined over time.",
            readme.ReplaceLineEndings(" "),
            StringComparison.Ordinal);
        Assert.DoesNotContain("Polish", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("Simplified Chinese", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("Japanese", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsPublisherEnforcesTheReleasePackageContract()
    {
        var script = File.ReadAllText(ProductPath("scripts", "publish-windows.ps1"));

        string[] requiredFragments =
        [
            "'restore', $solutionPath, '--locked-mode'",
            "'build', $solutionPath",
            "'test', $solutionPath",
            "'publish', $projectPath",
            "'--configuration', 'Release'",
            "'--runtime', $runtimeIdentifier",
            "'--self-contained', 'true'",
            "'-p:PublishSingleFile=false'",
            "'-p:PublishTrimmed=false'",
            "'-p:PublishAot=false'",
            "'-p:DebugSymbols=false'",
            "'-p:DebugType=None'",
            "'LICENSE'",
            "THIRD_PARTY_NOTICES.md",
            "DOTNET_THIRD_PARTY_NOTICES.txt",
            "SKIASHARP_THIRD_PARTY_NOTICES.txt",
            "Get-ChildItem -LiteralPath $packageDirectory -Recurse -File -Filter '*.pdb'",
            "Package file contains the private build path",
            "[IO.Compression.ZipArchive]::new",
            "Replace([IO.Path]::DirectorySeparatorChar, '/')",
            "Inno Setup 7.1.0 is required",
            "ExtSieve-Setup-$version-$runtimeIdentifier.exe",
            "packaging\\windows\\ExtSieve.iss",
            ".manifest.sha256",
        ];

        Assert.All(requiredFragments, fragment =>
            Assert.Contains(fragment, script, StringComparison.Ordinal));
        Assert.DoesNotContain("--self-contained', 'false", script, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxPublisherEnforcesTheReleasePackageContract()
    {
        var script = File.ReadAllText(ProductPath("scripts", "publish-linux.sh"));

        string[] requiredFragments =
        [
            "dotnet restore \"$solution_path\" --locked-mode",
            "dotnet build \"$solution_path\" --configuration Release --no-restore",
            "dotnet test \"$solution_path\" --configuration Release --no-build --no-restore",
            "dotnet publish \"$project_path\"",
            "runtime_identifier=linux-x64",
            "--self-contained true",
            "-p:PublishSingleFile=false",
            "-p:PublishTrimmed=false",
            "-p:PublishAot=false",
            "-p:DebugSymbols=false",
            "-p:DebugType=None",
            "for document in LICENSE README.md",
            "LINUX_REQUIREMENTS.md",
            "extsieve-app-icon-256.png",
            "DOTNET_THIRD_PARTY_NOTICES.txt",
            "SKIASHARP_THIRD_PARTY_NOTICES.txt",
            "find \"$package_directory\" -type f -name '*.pdb' -delete",
            "Package file contains the private build path",
            "--sort=name",
            "--mode=0755",
            "EXTSIEVE_REQUIRE_ZERO_SKIPS",
            "nfpm --version",
            "nfpm package --config",
            "extsieve_${version}_amd64.deb",
            "extsieve-${version}-1.x86_64.rpm",
            ".manifest.sha256",
        ];

        Assert.All(requiredFragments, fragment =>
            Assert.Contains(fragment, script, StringComparison.Ordinal));
        Assert.DoesNotContain("--self-contained false", script, StringComparison.Ordinal);
        Assert.DoesNotContain("docker", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowsInstallerUsesAStablePerUserIdentityAndSafeLifecyclePolicy()
    {
        var definition = File.ReadAllText(ProductPath(
            "packaging", "windows", "ExtSieve.iss"));
        var lifecycleTest = File.ReadAllText(ProductPath(
            "scripts", "test-windows-installer.ps1"));

        string[] definitionFragments =
        [
            "#define AppId \"{{E85F83F4-50BD-4B20-8B81-8D3793EAE130}\"",
            "AppId={#AppId}",
            "DefaultDirName={localappdata}\\Programs\\{#AppName}",
            "PrivilegesRequired=lowest",
            "ArchitecturesAllowed=x64compatible",
            "CloseApplications=yes",
            "Name: \"desktopicon\"",
            "Flags: unchecked",
            "CheckForMutexes('{#AppMutexName}')",
            "Close the application, then start the uninstall again.",
        ];

        Assert.All(definitionFragments, fragment =>
            Assert.Contains(fragment, definition, StringComparison.Ordinal));
        Assert.Contains("VersionInfoProductVersion={#AppVersion}", definition,
            StringComparison.Ordinal);
        Assert.Contains("Installed Apps", lifecycleTest, StringComparison.Ordinal);
        Assert.Contains("1.0.1", lifecycleTest, StringComparison.Ordinal);
        Assert.Contains("settings", lifecycleTest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("archive", lifecycleTest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NativeLinuxPackagesShareOnePinnedBuilderAndPolicy()
    {
        var config = File.ReadAllText(ProductPath("packaging", "linux", "nfpm.yaml"));
        var installer = File.ReadAllText(ProductPath("scripts", "install-nfpm.sh"));

        string[] packageFragments =
        [
            "maintainer: Giuseppe Russo <extsieve@outlook.com>",
            "version_schema: semver",
            "dst: /usr/lib/extsieve",
            "dst: /usr/bin/extsieve",
            "libc6 (>= 2.27)",
            "libicu70 | libicu72 | libicu74 | libicu76 | libicu78",
            "libssl3t64 | libssl3",
            "overrides:",
            "rpm:",
            "krb5-libs",
            "libX11",
        ];

        Assert.All(packageFragments, fragment =>
            Assert.Contains(fragment, config, StringComparison.Ordinal));
        Assert.Contains("nfpm_version=2.47.0", installer, StringComparison.Ordinal);
        Assert.Contains("checksums_sha256=", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet-runtime", config, StringComparison.Ordinal);
        Assert.DoesNotContain("aspnetcore-runtime", config, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet-sdk", config, StringComparison.Ordinal);
        Assert.Contains("glibc >= 2.27", config, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxLifecycleGateExercisesRuntimeArchivesAndRemoval()
    {
        var script = File.ReadAllText(ProductPath(
            "scripts", "test-linux-package-lifecycle.sh"));
        var smokeProject = XDocument.Load(ProductPath(
            "tests", "ExtSieve.PackageSmoke", "ExtSieve.PackageSmoke.csproj"));
        var smokeProgram = File.ReadAllText(ProductPath(
            "tests", "ExtSieve.PackageSmoke", "Program.cs"));

        string[] lifecycleFragments =
        [
            "PACKAGE_SHA256=",
            "apt-get install --yes \"$package\"",
            "path-include=/usr/share/doc/extsieve/*",
            "dnf --setopt=tsflags= install --assumeyes \"$package\"",
            "desktop-file-validate /usr/share/applications/extsieve.desktop",
            "NATIVE_OPTIONAL_UNRESOLVED=",
            "NATIVE_GLIBC_MAX=",
            "NATIVE_GLIBCXX_MAX=",
            "launch_application /usr/bin/extsieve",
            "apt-get remove --yes extsieve",
            "dnf remove --assumeyes extsieve",
            "test ! -e /usr/lib/extsieve",
            "test -f \"$settings_file\"",
            "PORTABLE_SMOKE=PASS",
            "PACKAGE_LIFECYCLE=PASS",
        ];

        Assert.All(lifecycleFragments, fragment =>
            Assert.Contains(fragment, script, StringComparison.Ordinal));
        Assert.Equal(
            "linux-x64",
            smokeProject.Descendants("RuntimeIdentifiers").Single().Value);
        Assert.Contains("new FolderScanner()", smokeProgram, StringComparison.Ordinal);
        Assert.Contains("new ArchivePlanner()", smokeProgram, StringComparison.Ordinal);
        Assert.Contains("new ZipArchiveService()", smokeProgram, StringComparison.Ordinal);
        Assert.Contains(
            "ExtSieve package archive smoke: PASS",
            smokeProgram,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxLauncherMetadataMatchesThePortablePackage()
    {
        var launcher = File.ReadAllText(ProductPath("packaging", "linux", "extsieve"));
        var desktopEntry = File.ReadAllText(ProductPath(
            "packaging", "linux", "extsieve.desktop"));

        Assert.Contains("exec \"$application_directory/ExtSieve.App\" \"$@\"", launcher,
            StringComparison.Ordinal);
        Assert.Contains("Type=Application", desktopEntry, StringComparison.Ordinal);
        Assert.Contains("Exec=extsieve", desktopEntry, StringComparison.Ordinal);
        Assert.Contains("TryExec=extsieve", desktopEntry, StringComparison.Ordinal);
        Assert.Contains("Icon=extsieve", desktopEntry, StringComparison.Ordinal);
        Assert.Contains("Terminal=false", desktopEntry, StringComparison.Ordinal);
    }

    private static string ProductPath(params string[] parts) => Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));

}
