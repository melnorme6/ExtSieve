using ExtSieve.App.Services;
using ExtSieve.App.Settings;
using ExtSieve.App.Tests.Support;

namespace ExtSieve.App.Tests.ViewModels;

public sealed class AboutUpdatesViewModelTests
{
    [Fact]
    public async Task InitializationNavigationAndPreferenceChangesNeverCheckForUpdates()
    {
        using var fixture = new MainWindowFixture();
        await fixture.ViewModel.InitializeAsync();
        for (var index = 0; index < 3; index++)
        {
            fixture.ViewModel.OpenSettings();
            fixture.ViewModel.SelectedThemeOption = fixture.ViewModel.ThemeOptions.Single(option => option.Value == ThemeMode.Dark);
            fixture.ViewModel.SelectedLanguageOption = fixture.ViewModel.LanguageOptions.Single(option => option.Value == LanguageMode.Italian);
            fixture.ViewModel.CloseSettings();
        }
        Assert.Equal(0, fixture.Updates.CallCount);
        Assert.True(fixture.ViewModel.ShowsCheckForUpdates);
        Assert.False(fixture.ViewModel.HasUpdateStatus);
        Assert.False(fixture.ViewModel.HasNewRelease);
        Assert.True(fixture.ViewModel.CheckForUpdatesCommand.CanExecute(null));
        Assert.Equal("Controlla aggiornamenti", fixture.ViewModel.CheckForUpdatesLabel);
    }

    [Fact]
    public async Task PendingCheckDisablesDuplicateRequestsThenAllowsRetry()
    {
        using var fixture = new MainWindowFixture();
        var completion = new TaskCompletionSource<UpdateCheckResult>();
        fixture.Updates.Handler = _ => completion.Task;
        var pending = fixture.ViewModel.CheckForUpdatesAsync();
        Assert.False(fixture.ViewModel.ShowsCheckForUpdates);
        Assert.True(fixture.ViewModel.HasUpdateStatus);
        Assert.Equal("Checking...", fixture.ViewModel.UpdateStatus);
        Assert.Equal("Checking...", fixture.ViewModel.CheckForUpdatesLabel);
        Assert.False(fixture.ViewModel.CheckForUpdatesCommand.CanExecute(null));
        await fixture.ViewModel.CheckForUpdatesAsync();
        fixture.ViewModel.CheckForUpdatesCommand.Execute(null);
        fixture.Localization.SetLanguage(LanguageMode.Italian);
        Assert.Equal("Controllo...", fixture.ViewModel.CheckForUpdatesLabel);
        Assert.Equal("Controllo...", fixture.ViewModel.UpdateStatus);
        Assert.Equal(1, fixture.Updates.CallCount);
        completion.SetResult(new(UpdateCheckOutcome.Failed));
        await pending;
        Assert.Equal("Impossibile controllare gli aggiornamenti.", fixture.ViewModel.UpdateStatus);
        Assert.False(fixture.ViewModel.ShowsCheckForUpdates);
        Assert.True(fixture.ViewModel.CheckForUpdatesCommand.CanExecute(null));
        fixture.Updates.Handler = _ => Task.FromResult(new UpdateCheckResult(UpdateCheckOutcome.UpToDate));
        await fixture.ViewModel.CheckForUpdatesAsync();
        Assert.Equal("ExtSieve è aggiornato.", fixture.ViewModel.UpdateStatus);
        Assert.False(fixture.ViewModel.ShowsCheckForUpdates);
        Assert.Equal(2, fixture.Updates.CallCount);
        Assert.False(fixture.ViewModel.HasNewRelease);
    }

    [Theory]
    [InlineData(LanguageMode.English)]
    [InlineData(LanguageMode.Italian)]
    [InlineData(LanguageMode.German)]
    [InlineData(LanguageMode.French)]
    [InlineData(LanguageMode.Spanish)]
    [InlineData(LanguageMode.PortugueseBrazil)]
    public async Task AvailableReleaseLocalizesLiveAndOpensOnlyOnASeparateUserAction(LanguageMode language)
    {
        using var fixture = new MainWindowFixture();
        var uri = new Uri("https://github.com/example/extsieve/releases/tag/v1.2.0");
        fixture.Updates.Handler = _ => Task.FromResult(new UpdateCheckResult(UpdateCheckOutcome.Available, "1.2.0", uri));
        await fixture.ViewModel.CheckForUpdatesAsync();
        Assert.Empty(fixture.ExternalResources.OpenedUris);
        Assert.True(fixture.ViewModel.HasNewRelease);
        Assert.False(fixture.ViewModel.ShowsCheckForUpdates);
        Assert.True(fixture.ViewModel.OpenDetectedReleaseCommand.CanExecute(null));
        fixture.Localization.SetLanguage(language);
        Assert.Equal(string.Format(fixture.Localization.CurrentCulture, fixture.Localization["UpdateAvailableFormat"], "1.2.0"),
            fixture.ViewModel.UpdateStatus);
        Assert.Equal(fixture.Localization["OpenDetectedRelease"], fixture.ViewModel.OpenDetectedReleaseLabel);
        Assert.True(fixture.ViewModel.HasUpdateStatus);
        await fixture.ViewModel.OpenDetectedReleaseAsync();
        Assert.Equal([uri], fixture.ExternalResources.OpenedUris);
        Assert.Empty(fixture.ExternalResources.OpenedFiles);
        fixture.Updates.Handler = _ => Task.FromResult(new UpdateCheckResult(UpdateCheckOutcome.Failed));
        await fixture.ViewModel.CheckForUpdatesAsync();
        Assert.False(fixture.ViewModel.HasNewRelease);
        Assert.False(fixture.ViewModel.OpenDetectedReleaseCommand.CanExecute(null));
        await fixture.ViewModel.OpenDetectedReleaseAsync();
        Assert.Single(fixture.ExternalResources.OpenedUris);
        Assert.Empty(fixture.ViewModel.TechnicalDetails);
        Assert.Equal(0, fixture.Scanner.CallCount);
        Assert.Equal(0, fixture.Zip.CallCount);
    }

    [Fact]
    public async Task ReopeningSettingsRestoresTheCheckActionWithoutIssuingARequest()
    {
        using var fixture = new MainWindowFixture();
        fixture.ViewModel.OpenSettings();
        fixture.Updates.Handler = _ => Task.FromResult(new UpdateCheckResult(UpdateCheckOutcome.Failed));
        await fixture.ViewModel.CheckForUpdatesAsync();
        Assert.False(fixture.ViewModel.ShowsCheckForUpdates);
        Assert.True(fixture.ViewModel.HasUpdateStatus);

        fixture.ViewModel.OpenSettings();
        Assert.False(fixture.ViewModel.ShowsCheckForUpdates);
        fixture.ViewModel.CloseSettings();
        fixture.ViewModel.OpenSettings();
        Assert.True(fixture.ViewModel.ShowsCheckForUpdates);
        Assert.False(fixture.ViewModel.HasUpdateStatus);
        Assert.Empty(fixture.ViewModel.UpdateStatus);
        Assert.True(fixture.ViewModel.CheckForUpdatesCommand.CanExecute(null));
        Assert.Equal(1, fixture.Updates.CallCount);

        fixture.Updates.Handler = _ => Task.FromResult(new UpdateCheckResult(UpdateCheckOutcome.UpToDate));
        await fixture.ViewModel.CheckForUpdatesAsync();
        Assert.Equal(2, fixture.Updates.CallCount);
        Assert.False(fixture.ViewModel.ShowsCheckForUpdates);
        Assert.Equal("ExtSieve is up to date.", fixture.ViewModel.UpdateStatus);
    }

    [Fact]
    public async Task ReopeningSettingsKeepsAnInFlightCheckAndResetsItsCompletedReleaseOnTheNextVisit()
    {
        using var fixture = new MainWindowFixture();
        var completion = new TaskCompletionSource<UpdateCheckResult>();
        fixture.Updates.Handler = _ => completion.Task;
        fixture.ViewModel.OpenSettings();
        var pending = fixture.ViewModel.CheckForUpdatesAsync();
        fixture.ViewModel.CloseSettings();
        fixture.ViewModel.OpenSettings();
        Assert.False(fixture.ViewModel.ShowsCheckForUpdates);
        Assert.Equal("Checking...", fixture.ViewModel.UpdateStatus);
        Assert.False(fixture.ViewModel.CheckForUpdatesCommand.CanExecute(null));
        Assert.Equal(1, fixture.Updates.CallCount);

        completion.SetResult(new(UpdateCheckOutcome.Available, "1.2.0",
            new Uri("https://github.com/example/extsieve/releases/tag/v1.2.0")));
        await pending;
        Assert.True(fixture.ViewModel.HasNewRelease);
        fixture.ViewModel.CloseSettings();
        fixture.ViewModel.OpenSettings();
        Assert.True(fixture.ViewModel.ShowsCheckForUpdates);
        Assert.False(fixture.ViewModel.HasNewRelease);
        Assert.False(fixture.ViewModel.OpenDetectedReleaseCommand.CanExecute(null));
        await fixture.ViewModel.OpenDetectedReleaseAsync();
        Assert.Empty(fixture.ExternalResources.OpenedUris);
        Assert.Equal(1, fixture.Updates.CallCount);
    }

    [Fact]
    public async Task ClosingTheApplicationCancelsTheRequestWithoutPublishingAResult()
    {
        using var fixture = new MainWindowFixture();
        var cancelled = false;
        fixture.Updates.Handler = async token =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { cancelled = token.IsCancellationRequested; throw; }
            return new(UpdateCheckOutcome.UpToDate);
        };
        var pending = fixture.ViewModel.CheckForUpdatesAsync();
        fixture.ViewModel.Dispose();
        await pending;
        Assert.True(cancelled);
        Assert.False(fixture.ViewModel.HasUpdateStatus);
        Assert.False(fixture.ViewModel.CheckForUpdatesCommand.CanExecute(null));
    }
}
