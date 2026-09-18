using ExtSieve.App.Settings;
using ExtSieve.App.Tests.Support;
using ExtSieve.App.ViewModels;
using ExtSieve.Core.Models;

namespace ExtSieve.App.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void AboutUsesAuthoritativeMetadataAndUnavailableFallbacks()
    {
        using var fixture = new MainWindowFixture();
        fixture.ProductVersion.PublicVersion = "1.0.0-rc.1";

        Assert.Equal("ExtSieve", fixture.ViewModel.ProductName);
        Assert.Equal("1.0.0-rc.1", fixture.ViewModel.ProductVersion);
        Assert.Equal("-", fixture.ViewModel.ProductBuildInfo);
        Assert.Equal("-", fixture.ViewModel.UpdateStatus);
        Assert.Equal("MIT License", fixture.ViewModel.ProductLicense);
        Assert.Equal("-", fixture.ViewModel.ReleasePage);
        Assert.Equal("-", fixture.ViewModel.SupportProject);
        Assert.Equal("-", fixture.ViewModel.ThirdPartyNotices);
    }

    [Fact]
    public async Task AboutOpensOnlyConfiguredResourcesWithoutExposingLocalPaths()
    {
        using var fixture = new MainWindowFixture();
        fixture.ProductAbout.LicenseDocumentPath = Path.Combine(fixture.RootPath, "LICENSE");
        fixture.ProductAbout.ThirdPartyNoticesPath = Path.Combine(
            fixture.RootPath,
            "THIRD_PARTY_NOTICES.md");
        fixture.ProductAbout.ReleasePageUri = new Uri(
            "https://github.com/public-example/extsieve-desktop/releases");
        fixture.ProductAbout.SupportPageUri = new Uri("https://ko-fi.com/melnorme");

        await fixture.ViewModel.OpenLicenseAsync();
        await fixture.ViewModel.OpenThirdPartyNoticesAsync();
        await fixture.ViewModel.OpenReleasePageAsync();
        await fixture.ViewModel.OpenSupportPageAsync();

        Assert.Equal(
            [fixture.ProductAbout.LicenseDocumentPath!, fixture.ProductAbout.ThirdPartyNoticesPath!],
            fixture.ExternalResources.OpenedFiles);
        Assert.Equal(
            [fixture.ProductAbout.ReleasePageUri, fixture.ProductAbout.SupportPageUri],
            fixture.ExternalResources.OpenedUris);
        Assert.Equal(
            fixture.ProductAbout.ReleasePageUri.AbsoluteUri,
            fixture.ViewModel.ReleasePage);
        Assert.Equal("Donate on Ko-fi", fixture.ViewModel.SupportProject);
        Assert.DoesNotContain(fixture.RootPath, fixture.ViewModel.ThirdPartyNotices,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InitialStateRequiresSourceAndSelection()
    {
        using var fixture = new MainWindowFixture();

        Assert.Equal(MainWindowState.NoSource, fixture.ViewModel.State);
        Assert.True(fixture.ViewModel.ChooseFolderCommand.CanExecute(null));
        Assert.False(fixture.ViewModel.ScanAgainCommand.CanExecute(null));
        Assert.False(fixture.ViewModel.CreateArchiveCommand.CanExecute(null));
        Assert.False(fixture.ViewModel.HasScanSummary);
        Assert.True(fixture.ViewModel.ShowsEmptyState);
        Assert.False(string.IsNullOrWhiteSpace(fixture.ViewModel.EmptyStateTitle));
        Assert.False(string.IsNullOrWhiteSpace(fixture.ViewModel.EmptyStateDescription));
    }

    [Fact]
    public async Task ChoosingFolderScansAndEnablesCreationAfterSelection()
    {
        using var fixture = new MainWindowFixture();
        var file = fixture.CreateScannedFile("nested/file.txt");
        fixture.Picker.SourcePath = fixture.RootPath;
        fixture.Scanner.Handler = (_, progress, _) =>
        {
            progress?.Report(1);
            return Task.FromResult(MainWindowFixture.Result(file));
        };

        await fixture.ViewModel.ChooseFolderAsync();

        Assert.Equal(MainWindowState.Results, fixture.ViewModel.State);
        Assert.Equal(Path.GetFullPath(fixture.RootPath), fixture.ViewModel.SourcePath);
        Assert.Equal(
            Path.Combine(Path.GetDirectoryName(fixture.RootPath)!, $"{Path.GetFileName(fixture.RootPath)}.zip"),
            fixture.ViewModel.OutputPath);
        Assert.Single(fixture.ViewModel.Groups.AllRows);
        Assert.False(fixture.ViewModel.CanCreateArchive);

        fixture.ViewModel.Groups.AllRows[0].IsSelected = true;

        Assert.True(fixture.ViewModel.CanCreateArchive);
        Assert.True(fixture.ViewModel.CreateArchiveCommand.CanExecute(null));
    }

    [Fact]
    public async Task EmptyAndWarningScanStatesRemainActionable()
    {
        using var fixture = new MainWindowFixture();
        fixture.Picker.SourcePath = fixture.RootPath;

        await fixture.ViewModel.ChooseFolderAsync();
        Assert.Equal(MainWindowState.EmptyResult, fixture.ViewModel.State);
        Assert.True(fixture.ViewModel.ShowsEmptyState);
        Assert.True(fixture.ViewModel.ScanAgainCommand.CanExecute(null));

        var file = fixture.CreateScannedFile("file.txt");
        fixture.Scanner.Handler = (_, _, _) => Task.FromResult(new ScanResult(
            [file],
            [new ExtensionGroup(".txt", 1, file.Size)],
            [new ScanWarning(ScanWarningKind.UnreadableFile, file.AbsolutePath)],
            TimeSpan.Zero));

        await fixture.ViewModel.ScanAgainAsync();

        Assert.Equal(MainWindowState.ResultsWithWarnings, fixture.ViewModel.State);
        Assert.False(fixture.ViewModel.ShowsEmptyState);
        Assert.True(fixture.ViewModel.HasWarnings);
        Assert.False(string.IsNullOrWhiteSpace(fixture.ViewModel.WarningSummary));
    }

    [Fact]
    public async Task ScanCancellationClearsBusyStateAndDoesNotExposeResults()
    {
        using var fixture = new MainWindowFixture();
        fixture.Picker.SourcePath = fixture.RootPath;
        fixture.Scanner.Handler = async (_, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return MainWindowFixture.Result();
        };

        var scan = fixture.ViewModel.ChooseFolderAsync();
        Assert.Equal(MainWindowState.Scanning, fixture.ViewModel.State);
        Assert.False(fixture.ViewModel.ChooseFolderCommand.CanExecute(null));

        fixture.ViewModel.CancelActiveOperation();
        await scan;

        Assert.Equal(MainWindowState.Cancelled, fixture.ViewModel.State);
        Assert.False(fixture.ViewModel.HasResults);
        Assert.False(fixture.ViewModel.IsBusy);
    }

    [Fact]
    public async Task SuccessfulArchiveExposesResultAndDestinationFolder()
    {
        using var fixture = await ScannedFixtureAsync();
        fixture.ViewModel.Groups.SelectAll();

        await fixture.ViewModel.CreateArchiveAsync();

        Assert.Equal(MainWindowState.Success, fixture.ViewModel.State);
        Assert.Equal(1, fixture.Zip.CallCount);
        Assert.False(fixture.Zip.LastReplaceExistingDestination);
        Assert.False(string.IsNullOrWhiteSpace(fixture.ViewModel.SuccessSummary));
        Assert.True(fixture.ViewModel.OpenDestinationFolderCommand.CanExecute(null));

        await fixture.ViewModel.OpenDestinationFolderAsync();
        Assert.Equal(Path.GetDirectoryName(fixture.ViewModel.OutputPath), fixture.Picker.OpenedFolder);
    }

    [Fact]
    public async Task ArchiveCancellationCannotStartAConcurrentArchive()
    {
        using var fixture = await ScannedFixtureAsync();
        fixture.ViewModel.Groups.SelectAll();
        fixture.Zip.Handler = async (_, _, _, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        };

        var creation = fixture.ViewModel.CreateArchiveAsync();
        Assert.Equal(MainWindowState.CreatingArchive, fixture.ViewModel.State);
        Assert.False(fixture.ViewModel.CreateArchiveCommand.CanExecute(null));

        await fixture.ViewModel.CreateArchiveAsync();
        Assert.Equal(1, fixture.Zip.CallCount);
        fixture.ViewModel.CancelActiveOperation();
        await creation;

        Assert.Equal(MainWindowState.Cancelled, fixture.ViewModel.State);
        Assert.False(fixture.ViewModel.IsBusy);
    }

    [Fact]
    public async Task ExistingDestinationRequiresPositiveConfirmation()
    {
        using var fixture = await ScannedFixtureAsync();
        fixture.ViewModel.Groups.SelectAll();
        File.WriteAllText(fixture.ViewModel.OutputPath, "existing");
        fixture.Prompt.Response = false;

        await fixture.ViewModel.CreateArchiveAsync();

        Assert.Equal(1, fixture.Prompt.CallCount);
        Assert.Equal(0, fixture.Zip.CallCount);
        Assert.Equal(MainWindowState.Results, fixture.ViewModel.State);
    }

    [Fact]
    public async Task ApprovedReplacementIsPassedExplicitlyToArchiveWriter()
    {
        using var fixture = await ScannedFixtureAsync();
        fixture.ViewModel.Groups.SelectAll();
        File.WriteAllText(fixture.ViewModel.OutputPath, "existing");

        await fixture.ViewModel.CreateArchiveAsync();

        Assert.Equal(1, fixture.Prompt.CallCount);
        Assert.Equal(1, fixture.Zip.CallCount);
        Assert.True(fixture.Zip.LastReplaceExistingDestination);
    }

    [Theory]
    [InlineData(LanguageMode.English)]
    [InlineData(LanguageMode.Italian)]
    [InlineData(LanguageMode.German)]
    [InlineData(LanguageMode.French)]
    [InlineData(LanguageMode.Spanish)]
    [InlineData(LanguageMode.PortugueseBrazil)]
    public async Task ReplacementConfirmationUsesEveryActiveLanguage(LanguageMode language)
    {
        using var fixture = new MainWindowFixture(language);
        var file = fixture.CreateScannedFile("file.txt");
        fixture.Picker.SourcePath = fixture.RootPath;
        fixture.Scanner.Handler = (_, _, _) => Task.FromResult(MainWindowFixture.Result(file));
        await fixture.ViewModel.ChooseFolderAsync();
        fixture.ViewModel.Groups.SelectAll();
        await File.WriteAllTextAsync(
            fixture.ViewModel.OutputPath,
            "existing",
            TestContext.Current.CancellationToken);
        fixture.Prompt.Response = false;

        await fixture.ViewModel.CreateArchiveAsync();

        Assert.Equal(fixture.Localization["ReplaceArchiveTitle"], fixture.Prompt.LastTitle);
        Assert.Equal(
            string.Format(
                fixture.Localization.CurrentCulture,
                fixture.Localization["ReplaceArchiveMessageFormat"],
                fixture.ViewModel.OutputPath),
            fixture.Prompt.LastMessage);
        Assert.Equal(fixture.Localization["Replace"], fixture.Prompt.LastConfirmLabel);
        Assert.Equal(fixture.Localization["Cancel"], fixture.Prompt.LastCancelLabel);
    }

    [Fact]
    public async Task NewSourceInvalidatesEarlierSelectionAndResult()
    {
        using var fixture = await ScannedFixtureAsync();
        fixture.ViewModel.Groups.SelectAll();
        var secondSource = Path.Combine(fixture.RootPath, "second");
        Directory.CreateDirectory(secondSource);
        fixture.Picker.SourcePath = secondSource;
        fixture.Scanner.Handler = (_, _, _) => Task.FromResult(MainWindowFixture.Result());

        await fixture.ViewModel.ChooseFolderAsync();

        Assert.Equal(MainWindowState.EmptyResult, fixture.ViewModel.State);
        Assert.Empty(fixture.ViewModel.Groups.AllRows);
        Assert.False(fixture.ViewModel.CanCreateArchive);
    }

    [Fact]
    public async Task InitializationAndSettingChangesApplyAndPersistIndependently()
    {
        using var fixture = new MainWindowFixture();
        fixture.Settings.Settings = new AppSettings
        {
            Language = LanguageMode.Italian,
            Theme = ThemeMode.Dark,
        };

        await fixture.ViewModel.InitializeAsync();

        Assert.Equal("it", fixture.Localization.CurrentCulture.Name);
        Assert.Equal(ThemeMode.Dark, Assert.Single(fixture.Theme.AppliedModes));
        fixture.ViewModel.SelectedLanguageOption = fixture.ViewModel.LanguageOptions
            .Single(option => option.Value == LanguageMode.English);
        fixture.ViewModel.SelectedThemeOption = fixture.ViewModel.ThemeOptions
            .Single(option => option.Value == ThemeMode.Light);

        Assert.Equal("en", fixture.Localization.CurrentCulture.Name);
        Assert.Equal(ThemeMode.Light, fixture.Theme.AppliedModes[^1]);
        Assert.Contains(fixture.Settings.SavedSettings, settings =>
            settings.Language == LanguageMode.English && settings.Theme == ThemeMode.Light);
    }

    [Fact]
    public async Task ControlSelectionFeedbackCannotOverwriteSettingsDuringInitialization()
    {
        using var fixture = new MainWindowFixture();
        fixture.Settings.Settings = new AppSettings
        {
            Language = LanguageMode.Italian,
            Theme = ThemeMode.Dark,
        };
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(MainWindowViewModel.SelectedLanguageOption))
            {
                fixture.ViewModel.SelectedLanguageOption = fixture.ViewModel.LanguageOptions
                    .Single(option => option.Value == LanguageMode.System);
            }

            if (eventArgs.PropertyName == nameof(MainWindowViewModel.SelectedThemeOption))
            {
                fixture.ViewModel.SelectedThemeOption = fixture.ViewModel.ThemeOptions
                    .Single(option => option.Value == ThemeMode.System);
            }
        };

        await fixture.ViewModel.InitializeAsync();

        Assert.Equal(LanguageMode.Italian, fixture.ViewModel.SelectedLanguageOption.Value);
        Assert.Equal(ThemeMode.Dark, fixture.ViewModel.SelectedThemeOption.Value);
        Assert.Empty(fixture.Settings.SavedSettings);
    }

    [Fact]
    public async Task SortHeadersExposeTablerChevronStatesAndDescriptiveAccessibleNames()
    {
        using var fixture = await ScannedFixtureAsync();

        Assert.Equal("Extension", fixture.ViewModel.ExtensionHeader);
        Assert.True(fixture.ViewModel.IsExtensionSortAscending);
        Assert.False(fixture.ViewModel.IsExtensionSortDescending);
        Assert.False(fixture.ViewModel.IsFileCountSortAscending);
        Assert.Contains("ascending", fixture.ViewModel.ExtensionHeaderAccessibleName);

        fixture.ViewModel.SortByExtensionCommand.Execute(null);

        Assert.True(fixture.ViewModel.IsExtensionSortDescending);
        Assert.False(fixture.ViewModel.IsExtensionSortAscending);
        Assert.Contains("descending", fixture.ViewModel.ExtensionHeaderAccessibleName);

        fixture.ViewModel.SortByFileCountCommand.Execute(null);

        Assert.False(fixture.ViewModel.IsExtensionSortAscending);
        Assert.False(fixture.ViewModel.IsExtensionSortDescending);
        Assert.True(fixture.ViewModel.IsFileCountSortAscending);
        Assert.Equal("Extension", fixture.ViewModel.ExtensionHeaderAccessibleName);
        Assert.Contains("ascending", fixture.ViewModel.FileCountHeaderAccessibleName);
    }

    [Fact]
    public async Task SettingsNavigationPreservesWorkflowStateAndReturnsSafely()
    {
        using var fixture = await ScannedFixtureAsync();
        fixture.ViewModel.Groups.AllRows[0].IsSelected = true;
        fixture.ViewModel.FilterText = "txt";
        fixture.ViewModel.Flatten = true;
        var source = fixture.ViewModel.SourcePath;
        var output = fixture.ViewModel.OutputPath;

        fixture.ViewModel.OpenSettingsCommand.Execute(null);

        Assert.True(fixture.ViewModel.IsSettingsViewOpen);
        Assert.False(fixture.ViewModel.IsMainViewOpen);
        Assert.Contains("preferences", fixture.ViewModel.StatusText);
        fixture.ViewModel.CloseSettingsCommand.Execute(null);

        Assert.False(fixture.ViewModel.IsSettingsViewOpen);
        Assert.True(fixture.ViewModel.IsMainViewOpen);
        Assert.Equal(source, fixture.ViewModel.SourcePath);
        Assert.Equal(output, fixture.ViewModel.OutputPath);
        Assert.Equal("txt", fixture.ViewModel.FilterText);
        Assert.True(fixture.ViewModel.Flatten);
        Assert.True(fixture.ViewModel.Groups.AllRows[0].IsSelected);
    }

    [Fact]
    public async Task HeaderSelectionCommandUsesGlobalTriStateLabels()
    {
        using var fixture = new MainWindowFixture();
        var text = fixture.CreateScannedFile("file.txt");
        var json = fixture.CreateScannedFile("file.json");
        fixture.Picker.SourcePath = fixture.RootPath;
        fixture.Scanner.Handler = (_, _, _) => Task.FromResult(MainWindowFixture.Result(text, json));
        await fixture.ViewModel.ChooseFolderAsync();
        fixture.ViewModel.Groups.AllRows[0].IsSelected = true;
        fixture.ViewModel.FilterText = "json";
        var summaryNotifications = 0;
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(MainWindowViewModel.SelectionSummary))
            {
                summaryNotifications++;
            }
        };
        var plannerCalls = fixture.Planner.CallCount;

        Assert.Null(fixture.ViewModel.AllGroupsSelectionState);
        Assert.Contains("Some", fixture.ViewModel.SelectionToggleToolTip);
        fixture.ViewModel.ToggleAllSelectionCommand.Execute(null);

        Assert.True(fixture.ViewModel.AllGroupsSelectionState);
        Assert.All(fixture.ViewModel.Groups.AllRows, row => Assert.True(row.IsSelected));
        Assert.Contains("Clear", fixture.ViewModel.SelectionToggleAccessibleName);
        Assert.Equal(plannerCalls + 1, fixture.Planner.CallCount);
        Assert.Equal(1, summaryNotifications);

        fixture.ViewModel.ToggleAllSelectionCommand.Execute(null);

        Assert.False(fixture.ViewModel.AllGroupsSelectionState);
        Assert.All(fixture.ViewModel.Groups.AllRows, row => Assert.False(row.IsSelected));
        Assert.Equal(2, summaryNotifications);
    }

    [Fact]
    public async Task ClearFilterVisibilityAndCommandFollowFilterContent()
    {
        using var fixture = await ScannedFixtureAsync();

        Assert.False(fixture.ViewModel.IsFilterClearVisible);
        Assert.False(fixture.ViewModel.ClearFilterCommand.CanExecute(null));

        fixture.ViewModel.FilterText = "txt";

        Assert.True(fixture.ViewModel.IsFilterClearVisible);
        Assert.True(fixture.ViewModel.ClearFilterCommand.CanExecute(null));

        fixture.ViewModel.ClearFilterCommand.Execute(null);

        Assert.Equal(string.Empty, fixture.ViewModel.FilterText);
        Assert.False(fixture.ViewModel.IsFilterClearVisible);
        Assert.False(fixture.ViewModel.ClearFilterCommand.CanExecute(null));
    }

    [Fact]
    public async Task ClearFilterRestoresRowsAndPreservesHiddenSelectionAndGlobalTriState()
    {
        using var fixture = new MainWindowFixture();
        var text = fixture.CreateScannedFile("file.txt");
        var json = fixture.CreateScannedFile("file.json");
        fixture.Picker.SourcePath = fixture.RootPath;
        fixture.Scanner.Handler = (_, _, _) => Task.FromResult(MainWindowFixture.Result(text, json));
        await fixture.ViewModel.ChooseFolderAsync();
        fixture.ViewModel.Groups.AllRows.Single(row => row.ExtensionKey == ".txt").IsSelected = true;
        fixture.ViewModel.FilterText = "json";

        Assert.Single(fixture.ViewModel.Groups.VisibleRows);
        Assert.Null(fixture.ViewModel.AllGroupsSelectionState);

        fixture.ViewModel.ClearFilterCommand.Execute(null);

        Assert.Equal(2, fixture.ViewModel.Groups.VisibleRows.Count);
        Assert.True(fixture.ViewModel.Groups.AllRows.Single(row => row.ExtensionKey == ".txt").IsSelected);
        Assert.False(fixture.ViewModel.Groups.AllRows.Single(row => row.ExtensionKey == ".json").IsSelected);
        Assert.Null(fixture.ViewModel.AllGroupsSelectionState);
    }

    [Fact]
    public void SettingsSaveFailureIsNonBlockingAndVisible()
    {
        using var fixture = new MainWindowFixture();
        fixture.Settings.SaveException = new IOException("Synthetic settings failure.");

        fixture.ViewModel.SelectedThemeOption = fixture.ViewModel.ThemeOptions
            .Single(option => option.Value == ThemeMode.Dark);

        Assert.Equal(MainWindowState.NoSource, fixture.ViewModel.State);
        Assert.True(fixture.ViewModel.HasSettingsWarning);
        Assert.False(string.IsNullOrWhiteSpace(fixture.ViewModel.SettingsWarning));
        Assert.Equal(ThemeMode.Dark, fixture.Theme.AppliedModes[^1]);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1024, "1.0 KiB")]
    [InlineData(1572864, "1.5 MiB")]
    public void ByteSizeFormatterUsesBinaryUnits(long bytes, string expected)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("en");
        Assert.Equal(expected, ByteSizeFormatter.Format(bytes, culture));
    }

    [Fact]
    public void ByteSizeFormatterUsesActiveCultureDecimalSeparator()
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("it");
        Assert.Equal("1,5 KiB", ByteSizeFormatter.Format(1536, culture));
    }

    private static async Task<MainWindowFixture> ScannedFixtureAsync()
    {
        var fixture = new MainWindowFixture();
        var file = fixture.CreateScannedFile("file.txt");
        fixture.Picker.SourcePath = fixture.RootPath;
        fixture.Scanner.Handler = (_, _, _) => Task.FromResult(MainWindowFixture.Result(file));
        await fixture.ViewModel.ChooseFolderAsync();
        return fixture;
    }
}
