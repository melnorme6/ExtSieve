using ExtSieve.App.Settings;
using ExtSieve.App.Tests.Support;

namespace ExtSieve.App.Tests.ViewModels;

public sealed class PublicBuildInfoViewModelTests
{
    [Theory]
    [InlineData(LanguageMode.English, "Development build")]
    [InlineData(LanguageMode.Italian, "Build di sviluppo")]
    [InlineData(LanguageMode.German, "Entwicklungsbuild")]
    [InlineData(LanguageMode.French, "Version de développement")]
    [InlineData(LanguageMode.Spanish, "Compilación de desarrollo")]
    [InlineData(LanguageMode.PortugueseBrazil, "Versão de desenvolvimento")]
    public async Task DevelopmentBuildIsLocalizedAndHasNoCommitAction(LanguageMode language, string expected)
    {
        using var fixture = new MainWindowFixture();
        fixture.Localization.SetLanguage(language);
        Assert.Equal(expected, fixture.ViewModel.ProductBuildInfo);
        Assert.False(fixture.ViewModel.HasPublicCommit);
        Assert.False(fixture.ViewModel.OpenPublicCommitCommand.CanExecute(null));
        await fixture.ViewModel.OpenPublicCommitAsync();
        Assert.Empty(fixture.ExternalResources.OpenedUris);
        Assert.Equal(0, fixture.Updates.CallCount);
    }

    [Fact]
    public async Task PublicCommitOpensOnlyOnExplicitActivationWithoutAnUpdateCheck()
    {
        using var fixture = new MainWindowFixture();
        fixture.ProductAbout.PublicSourceRevision = "abcdef1234567890abcdef1234567890abcdef12";
        fixture.ProductAbout.BuildInfo = "abcdef1";
        fixture.ProductAbout.CommitPageUri = new Uri("https://github.com/example/extsieve/commit/abcdef1234567890abcdef1234567890abcdef12");
        fixture.ViewModel.OpenSettings();
        Assert.Equal("abcdef1", fixture.ViewModel.ProductBuildInfo);
        Assert.True(fixture.ViewModel.HasPublicCommit);
        Assert.True(fixture.ViewModel.OpenPublicCommitCommand.CanExecute(null));
        Assert.Empty(fixture.ExternalResources.OpenedUris);
        fixture.Localization.SetLanguage(LanguageMode.Italian);
        Assert.Equal("Apri commit su GitHub", fixture.ViewModel.OpenPublicCommitToolTip);
        await fixture.ViewModel.OpenPublicCommitAsync();
        Assert.Equal([fixture.ProductAbout.CommitPageUri], fixture.ExternalResources.OpenedUris);
        Assert.Empty(fixture.ExternalResources.OpenedFiles);
        Assert.Equal(0, fixture.Updates.CallCount);
    }
}
