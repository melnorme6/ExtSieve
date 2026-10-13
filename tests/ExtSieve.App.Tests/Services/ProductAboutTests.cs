using System.Reflection;
using System.Reflection.Emit;
using ExtSieve.App;
using ExtSieve.App.Services;

namespace ExtSieve.App.Tests.Services;

public sealed class ProductAboutTests
{
    [Theory]
    [InlineData("abcdef1234567890abcdef1234567890abcdef12", "abcdef1")]
    [InlineData(null, null)]
    [InlineData("abcdef1", null)]
    public void EmbeddedPublicMetadataNeverFallsBackToInformationalVersionOrPrivateSourceRevision(string? revision, string? expected)
    {
        var assembly = CreateSyntheticAssembly(revision);
        var provider = new AssemblyProductAboutProvider(assembly, Path.GetTempPath());
        Assert.Equal(expected, provider.BuildInfo);
        Assert.Equal("1.1.0", new AssemblyProductVersionProvider(assembly).PublicVersion);
    }

    [Fact]
    public void AppAssemblyProvidesApprovedProductMetadata()
    {
        var provider = new AssemblyProductAboutProvider(typeof(MainWindow).Assembly);

        Assert.Equal("ExtSieve", provider.ProductName);
        Assert.Equal("MIT License", provider.LicenseName);
        var revision = typeof(MainWindow).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "PublicSourceRevision")?.Value;
        Assert.Equal(revision?.ToLowerInvariant(), provider.PublicSourceRevision);
        Assert.Equal(revision?[..7].ToLowerInvariant(), provider.BuildInfo);
        Assert.Equal(new Uri("https://github.com/melnorme6/ExtSieve/releases"), provider.ReleasePageUri);
        Assert.Equal(new Uri("https://ko-fi.com/melnorme"), provider.SupportPageUri);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("abcdef1", null)]
    [InlineData("abcdef1234567890abcdef1234567890abcdef12345", null)]
    [InlineData("zbcdef1234567890abcdef1234567890abcdef12", null)]
    [InlineData(" abcdef1234567890abcdef1234567890abcdef12", null)]
    [InlineData("1.1.0+abcdef1234567890abcdef1234567890abcdef12", null)]
    [InlineData("ABCDEF1234567890ABCDEF1234567890ABCDEF12", "abcdef1234567890abcdef1234567890abcdef12")]
    public void PublicRevisionRequiresAFullHexadecimalSha(
        string? revision,
        string? expected)
    {
        Assert.Equal(
            expected,
            AssemblyProductAboutProvider.GetValidatedPublicSourceRevision(revision));
    }

    [Theory]
    [InlineData("https://github.com/example/extsieve/releases", "https://github.com/example/extsieve/commit/abcdef1234567890abcdef1234567890abcdef12")]
    [InlineData("https://github.com/example/extsieve/releases/", "https://github.com/example/extsieve/commit/abcdef1234567890abcdef1234567890abcdef12")]
    [InlineData("https://code-host.invalid/example/extsieve/releases", null)]
    public void PublicCommitUsesTheConfiguredPublicRepositoryAndTheFullRevision(string releaseUrl, string? expectedUri)
    {
        var provider = new AssemblyProductAboutProvider(CreateSyntheticAssembly(
            "ABCDEF1234567890ABCDEF1234567890ABCDEF12", releaseUrl), Path.GetTempPath());
        Assert.Equal("abcdef1", provider.BuildInfo);
        Assert.Equal("abcdef1234567890abcdef1234567890abcdef12", provider.PublicSourceRevision);
        Assert.Equal(expectedUri is null ? null : new Uri(expectedUri), provider.CommitPageUri);
    }

    private static AssemblyBuilder CreateSyntheticAssembly(string? revision, string releaseUrl = "https://github.com/example/extsieve/releases")
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("SyntheticProduct"), AssemblyBuilderAccess.Run);
        const string privateRevision = "1111111111111111111111111111111111111111";
        assembly.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!, ["1.1.0+" + privateRevision]));
        foreach (var (key, value) in new[] { ("SourceRevisionId", privateRevision), ("PublicReleaseUrl", releaseUrl), ("PublicSourceRevision", revision) })
        {
            if (value is not null) assembly.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!, [key, value]));
        }
        return assembly;
    }

    [Theory]
    [InlineData("https://github.com/example-organization/extsieve-desktop-utility/releases", true)]
    [InlineData("https://github.com/example-organization/extsieve-desktop-utility/releases/", true)]
    [InlineData("https://github.com/example-organization/extsieve-desktop-utility", false)]
    [InlineData("http://github.com/example-organization/extsieve-desktop-utility/releases", false)]
    [InlineData("https://code-host.invalid/example/extsieve/releases", false)]
    [InlineData("https://github.com/example-organization/extsieve-desktop-utility/releases?private=value", false)]
    public void ReleasePageAllowsOnlyAConfiguredPublicGitHubReleasesUrl(
        string value,
        bool expected)
    {
        Assert.Equal(
            expected,
            AssemblyProductAboutProvider.GetPublicReleasePageUri(value) is not null);
    }

    [Theory]
    [InlineData("https://ko-fi.com/melnorme", true)]
    [InlineData("https://ko-fi.com/melnorme/", true)]
    [InlineData("http://ko-fi.com/melnorme", false)]
    [InlineData("https://example.com/melnorme", false)]
    [InlineData("https://ko-fi.com/melnorme/private", false)]
    [InlineData("https://ko-fi.com/melnorme?private=value", false)]
    public void SupportPageAllowsOnlyAPublicKofiProfileUrl(string value, bool expected)
    {
        Assert.Equal(
            expected,
            AssemblyProductAboutProvider.GetPublicSupportPageUri(value) is not null);
    }

    [Fact]
    public void LocalDocumentsAreExposedOnlyWhenTheyExistBesideTheApplication()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ExtSieve.App.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "LICENSE"), "MIT License");
            File.WriteAllText(
                Path.Combine(directory, "THIRD_PARTY_NOTICES.md"),
                "# Third-Party Notices");

            var provider = new AssemblyProductAboutProvider(
                typeof(MainWindow).Assembly,
                directory);

            Assert.Equal(Path.Combine(directory, "LICENSE"), provider.LicenseDocumentPath);
            Assert.Equal(
                Path.Combine(directory, "THIRD_PARTY_NOTICES.md"),
                provider.ThirdPartyNoticesPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
