using System.Reflection;
using ExtSieve.App;
using ExtSieve.App.Services;

namespace ExtSieve.App.Tests.Services;

public sealed class ProductAboutTests
{
    [Fact]
    public void AppAssemblyProvidesApprovedProductMetadata()
    {
        var provider = new AssemblyProductAboutProvider(typeof(MainWindow).Assembly);

        Assert.Equal("ExtSieve", provider.ProductName);
        Assert.Equal("MIT License", provider.LicenseName);
        Assert.Null(provider.BuildInfo);
        Assert.Null(provider.ReleasePageUri);
        Assert.Equal(new Uri("https://ko-fi.com/melnorme"), provider.SupportPageUri);
    }

    [Theory]
    [InlineData("1.0.0", null)]
    [InlineData("1.0.0-beta.1", null)]
    [InlineData("1.0.0+abcdef1", "abcdef1")]
    [InlineData("1.0.0-rc.1+abcdef123456.build.42", "abcdef1")]
    [InlineData("1.0.0+private-runner-name", null)]
    public void BuildInfoUsesOnlyASafeShortCommitFromBuildMetadata(
        string informationalVersion,
        string? expected)
    {
        Assert.Equal(
            expected,
            AssemblyProductAboutProvider.GetSafeBuildInfo(informationalVersion));
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
