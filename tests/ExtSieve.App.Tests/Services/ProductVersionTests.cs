using System.Diagnostics;
using ExtSieve.App;
using ExtSieve.App.Services;
using ExtSieve.App.Tests.Support;

namespace ExtSieve.App.Tests.Services;

public sealed class ProductVersionTests
{
    [Fact]
    public void AppAssemblyUsesTheCentralProductVersionMetadata()
    {
        var assembly = typeof(MainWindow).Assembly;
        var provider = new AssemblyProductVersionProvider(assembly);
        var fileVersion = FileVersionInfo.GetVersionInfo(assembly.Location);

        Assert.Equal(new Version(1, 0, 0, 0), assembly.GetName().Version);
        Assert.Equal("1.0.0.0", fileVersion.FileVersion);
        Assert.Equal("1.0.0", provider.InformationalVersion);
        Assert.Equal("1.0.0", provider.PublicVersion);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.0.3", "1.0.3")]
    [InlineData("1.0.0-beta.1", "1.0.0-beta.1")]
    [InlineData("1.0.0-rc.1", "1.0.0-rc.1")]
    [InlineData("1.0.0+abcdef", "1.0.0")]
    public void ProviderPreservesPublicSemVerAndRemovesBuildMetadata(
        string informationalVersion,
        string expectedPublicVersion)
    {
        var provider = new AssemblyProductVersionProvider(informationalVersion);

        Assert.Equal(informationalVersion, provider.InformationalVersion);
        Assert.Equal(expectedPublicVersion, provider.PublicVersion);
    }

    [Theory]
    [InlineData("1.0.0", "ExtSieve 1.0.0")]
    [InlineData("1.0.0-beta.1", "ExtSieve 1.0.0-beta.1")]
    [InlineData("1.0.0-rc.1+abcdef", "ExtSieve 1.0.0-rc.1")]
    public void WindowTitleUsesTheCleanPublicVersion(
        string informationalVersion,
        string expected)
    {
        var provider = new AssemblyProductVersionProvider(informationalVersion);

        Assert.Equal(
            expected,
            WindowTitleFormatter.Format("ExtSieve", provider.PublicVersion));
    }

    [Fact]
    public void WindowTitleRemainsStableAcrossSettingsNavigation()
    {
        using var fixture = new MainWindowFixture();

        var mainTitle = fixture.ViewModel.AppTitle;
        fixture.ViewModel.OpenSettings();

        Assert.True(fixture.ViewModel.IsSettingsViewOpen);
        Assert.Equal(mainTitle, fixture.ViewModel.AppTitle);

        fixture.ViewModel.CloseSettings();

        Assert.True(fixture.ViewModel.IsMainViewOpen);
        Assert.Equal(mainTitle, fixture.ViewModel.AppTitle);
    }
}
