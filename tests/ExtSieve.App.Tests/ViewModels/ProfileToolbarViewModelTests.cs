using ExtSieve.App.Settings;
using ExtSieve.App.Tests.Support;
using ExtSieve.App.ViewModels;
using ExtSieve.Core.Models;

namespace ExtSieve.App.Tests.ViewModels;

public sealed class ProfileToolbarViewModelTests
{
    [Theory]
    [InlineData(".txt", 1)]
    [InlineData("", 1)]
    [InlineData(".missing", 0)]
    public async Task CommittingProfileReplacesHiddenAndVisibleSelectionAndUpdatesPlan(string key, int selectedCount)
    {
        using var fixture = new MainWindowFixture();
        await ScanAsync(fixture, [key, ".absent"]);
        fixture.ViewModel.Groups.SelectAll();
        fixture.ViewModel.FilterText = "json";
        var plansBefore = fixture.Planner.CallCount;

        fixture.ViewModel.SelectProfileCommand.Execute(Assert.Single(fixture.ViewModel.Profiles.Profiles));

        Assert.Equal(selectedCount, fixture.ViewModel.Groups.SelectedGroupCount);
        Assert.False(Assert.Single(fixture.ViewModel.Groups.VisibleRows).IsSelected);
        Assert.Equal(selectedCount == 0 ? [] : new[] { key }, SelectedKeys(fixture));
        Assert.Equal(plansBefore + (selectedCount == 0 ? 0 : 1), fixture.Planner.CallCount);
        Assert.Equal(selectedCount > 0, fixture.ViewModel.CanCreateArchive);
        Assert.Equal(Format(fixture, "ProfileAppliedMissingFormat"), fixture.ViewModel.StatusText);
    }

    [Fact]
    public async Task ProgrammaticSelectionInitializationReloadAndSavingDoNotApplyProfiles()
    {
        using var fixture = new MainWindowFixture();
        await ScanAsync(fixture, [".txt"]);
        Assert.Empty(SelectedKeys(fixture));
        fixture.ViewModel.Groups.AllRows.Single(row => row.ExtensionKey == ".json").IsSelected = true;
        fixture.ViewModel.Profiles.SelectedProfile = Assert.Single(fixture.ViewModel.Profiles.Profiles);
        await fixture.ViewModel.Profiles.LoadAsync();
        fixture.FeatureDialogs.Name = "Current";
        await fixture.ViewModel.SaveProfileAsync();
        fixture.FeatureDialogs.Name = "Renamed";
        await fixture.ViewModel.Profiles.RenameAsync();
        fixture.FeatureDialogs.Name = "Duplicate";
        await fixture.ViewModel.Profiles.DuplicateAsync();

        Assert.Equal([".json"], SelectedKeys(fixture));
        Assert.Equal(fixture.Localization["StatusResults"], fixture.ViewModel.StatusText);
        Assert.Equal(["Duplicate", "Renamed", "Text"], (await fixture.Profiles.LoadAsync(TestContext.Current.CancellationToken)).Value.Select(profile => profile.Name));
    }

    [Theory]
    [InlineData(LanguageMode.English)]
    [InlineData(LanguageMode.Italian)]
    [InlineData(LanguageMode.German)]
    [InlineData(LanguageMode.French)]
    [InlineData(LanguageMode.Spanish)]
    [InlineData(LanguageMode.PortugueseBrazil)]
    public async Task FooterDistinguishesPresentAndAbsentKeysAndNextSelectionReplacesNotice(LanguageMode language)
    {
        using var fixture = new MainWindowFixture(language);
        await ScanAsync(fixture, [".TXT", ""]);
        fixture.ViewModel.SelectProfileCommand.Execute(Assert.Single(fixture.ViewModel.Profiles.Profiles));
        Assert.Equal(Format(fixture, "ProfileAppliedFormat"), fixture.ViewModel.StatusText);
        Assert.Equal(["", ".txt"], SelectedKeys(fixture));
        fixture.ViewModel.Groups.AllRows.Single(row => row.ExtensionKey == ".json").IsSelected = true;
        Assert.Equal(fixture.Localization["StatusResults"], fixture.ViewModel.StatusText);
    }

    [Fact]
    public async Task NewScanClearsProfileNoticeAndNeverReappliesPreviousProfile()
    {
        using var fixture = new MainWindowFixture();
        await ScanAsync(fixture, [".txt"]);
        fixture.ViewModel.SelectProfileCommand.Execute(Assert.Single(fixture.ViewModel.Profiles.Profiles));
        await fixture.ViewModel.ScanAgainAsync();
        Assert.Empty(SelectedKeys(fixture));
        Assert.Null(fixture.ViewModel.Profiles.SelectedProfile);
        Assert.Equal(fixture.Localization["StatusResults"], fixture.ViewModel.StatusText);
    }

    [Fact]
    public async Task ArchiveOperationReplacesTheAppliedProfileFooter()
    {
        using var fixture = new MainWindowFixture();
        await ScanAsync(fixture, [".txt"]);
        fixture.ViewModel.SelectProfileCommand.Execute(Assert.Single(fixture.ViewModel.Profiles.Profiles));
        await fixture.ViewModel.CreateArchiveAsync();
        Assert.Equal(MainWindowState.Success, fixture.ViewModel.State);
        Assert.Equal(fixture.Localization["StatusSuccess"], fixture.ViewModel.StatusText);
    }

    [Fact]
    public async Task ActiveScanBlocksCommitAndClearWithoutChangingSelection()
    {
        using var fixture = new MainWindowFixture();
        await ScanAsync(fixture, [".txt"]);
        var profile = Assert.Single(fixture.ViewModel.Profiles.Profiles);
        fixture.Scanner.Handler = async (_, _, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return MainWindowFixture.Result();
        };
        var scan = fixture.ViewModel.ScanAgainAsync();
        fixture.ViewModel.Profiles.SelectedProfile = profile;
        Assert.False(fixture.ViewModel.SelectProfileCommand.CanExecute(profile));
        Assert.False(fixture.ViewModel.ClearProfileCommand.CanExecute(null));
        fixture.ViewModel.SelectProfileCommand.Execute(profile);
        fixture.ViewModel.ClearProfileCommand.Execute(null);
        Assert.Same(profile, fixture.ViewModel.Profiles.SelectedProfile);
        Assert.Empty(SelectedKeys(fixture));
        Assert.Equal(0, fixture.FeatureDialogs.DeleteConfirmationCount);
        fixture.ViewModel.CancelActiveOperation();
        await scan;
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData("")]
    [InlineData(".missing")]
    public async Task ClearingPreservesLibraryPersistenceScanAndHiddenExtensionSelection(string key)
    {
        using var fixture = new MainWindowFixture();
        await ScanAsync(fixture, [key]);
        var profile = Assert.Single(fixture.ViewModel.Profiles.Profiles);
        fixture.ViewModel.SelectProfileCommand.Execute(profile);
        fixture.ViewModel.FilterText = "json";
        var selected = SelectedKeys(fixture);
        var rows = fixture.ViewModel.Groups.AllRows.ToArray();
        var state = fixture.ViewModel.State;
        var summary = fixture.ViewModel.SelectionSummary;
        var output = fixture.ViewModel.OutputPath;
        var totals = (fixture.ViewModel.TotalFilesDisplay, fixture.ViewModel.TotalGroupsDisplay, fixture.ViewModel.TotalBytesDisplay);
        var calls = (fixture.ProfileStore.LoadCount, fixture.ProfileStore.SaveCount, fixture.Scanner.CallCount, fixture.Planner.CallCount);
        var path = Path.Combine(fixture.RootPath, "local-data", "profiles.json");
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

        Assert.True(fixture.ViewModel.ClearProfileCommand.CanExecute(null));
        fixture.ViewModel.ClearProfileCommand.Execute(null);

        Assert.Null(fixture.ViewModel.Profiles.SelectedProfile);
        Assert.Same(profile, Assert.Single(fixture.ViewModel.Profiles.Profiles));
        Assert.Equal(selected, SelectedKeys(fixture));
        Assert.Equal(rows, fixture.ViewModel.Groups.AllRows);
        Assert.Equal("json", fixture.ViewModel.FilterText);
        Assert.False(Assert.Single(fixture.ViewModel.Groups.VisibleRows).IsSelected);
        Assert.Equal(state, fixture.ViewModel.State);
        Assert.Equal(summary, fixture.ViewModel.SelectionSummary);
        Assert.Equal(output, fixture.ViewModel.OutputPath);
        Assert.Equal(totals, (fixture.ViewModel.TotalFilesDisplay, fixture.ViewModel.TotalGroupsDisplay, fixture.ViewModel.TotalBytesDisplay));
        Assert.Equal(calls, (fixture.ProfileStore.LoadCount, fixture.ProfileStore.SaveCount, fixture.Scanner.CallCount, fixture.Planner.CallCount));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(0, fixture.FeatureDialogs.DeleteConfirmationCount);
        Assert.Equal(0, fixture.Prompt.CallCount);
        Assert.False(fixture.ViewModel.ClearProfileCommand.CanExecute(null));
        Assert.Equal(fixture.Localization["StatusResults"], fixture.ViewModel.StatusText);
    }

    [Fact]
    public async Task ClearIsDisabledWithoutProfileAndRepeatedExecutionDoesNothing()
    {
        using var fixture = new MainWindowFixture();
        Assert.False(fixture.ViewModel.ClearProfileCommand.CanExecute(null));
        await ScanAsync(fixture, [".txt"]);
        Assert.False(fixture.ViewModel.ClearProfileCommand.CanExecute(null));
        fixture.ViewModel.Groups.SelectAll();
        fixture.ViewModel.ClearProfileCommand.Execute(null);
        Assert.Equal(["", ".json", ".txt"], SelectedKeys(fixture));
        fixture.ViewModel.SelectProfileCommand.Execute(Assert.Single(fixture.ViewModel.Profiles.Profiles));
        fixture.ViewModel.ClearProfileCommand.Execute(null);
        var notifications = 0;
        fixture.ViewModel.Profiles.PropertyChanged += (_, _) => notifications++;
        fixture.ViewModel.ClearProfileCommand.Execute(null);
        Assert.Equal(0, notifications);
        Assert.Equal([".txt"], SelectedKeys(fixture));
        Assert.Equal(0, fixture.FeatureDialogs.DeleteConfirmationCount);
    }

    [Fact]
    public async Task ClearingBeforeScanPreservesIdleStateAndLibrary()
    {
        using var fixture = new MainWindowFixture();
        await fixture.Profiles.SaveAsync([ExtensionProfile.Create("Text", [".txt"])], TestContext.Current.CancellationToken);
        await fixture.ViewModel.InitializeAsync();
        var profile = Assert.Single(fixture.ViewModel.Profiles.Profiles);
        fixture.ViewModel.SelectProfileCommand.Execute(profile);
        var state = fixture.ViewModel.State;
        var status = fixture.ViewModel.StatusText;
        fixture.ViewModel.ClearProfileCommand.Execute(null);
        Assert.Null(fixture.ViewModel.Profiles.SelectedProfile);
        Assert.Same(profile, Assert.Single(fixture.ViewModel.Profiles.Profiles));
        Assert.Equal(state, fixture.ViewModel.State);
        Assert.Equal(status, fixture.ViewModel.StatusText);
        Assert.False(fixture.ViewModel.HasSource);
        Assert.False(fixture.ViewModel.HasResults);
        Assert.Equal(0, fixture.Scanner.CallCount);
        Assert.Equal(0, fixture.ProfileStore.SaveCount);
    }

    [Fact]
    public async Task ClearAndCommitAreDisabledWhileFeatureDialogIsOpen()
    {
        using var fixture = new MainWindowFixture();
        await ScanAsync(fixture, [".txt"]);
        var profile = Assert.Single(fixture.ViewModel.Profiles.Profiles);
        fixture.ViewModel.SelectProfileCommand.Execute(profile);
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.FeatureDialogs.EditHandler = _ => closed.Task;
        var dialog = fixture.ViewModel.EditExclusionsAsync();
        Assert.False(fixture.ViewModel.ClearProfileCommand.CanExecute(null));
        Assert.False(fixture.ViewModel.SelectProfileCommand.CanExecute(profile));
        fixture.ViewModel.ClearProfileCommand.Execute(null);
        Assert.Same(profile, fixture.ViewModel.Profiles.SelectedProfile);
        Assert.Equal([".txt"], SelectedKeys(fixture));
        Assert.Equal(0, fixture.FeatureDialogs.DeleteConfirmationCount);
        closed.SetResult(false);
        await dialog;
        Assert.True(fixture.ViewModel.ClearProfileCommand.CanExecute(null));
    }

    [Fact]
    public async Task ClearingRemainsAvailableWithUnavailableStorageAndNeverReadsOrWrites()
    {
        using var fixture = new MainWindowFixture();
        Directory.CreateDirectory(Path.Combine(fixture.RootPath, "local-data"));
        await File.WriteAllTextAsync(Path.Combine(fixture.RootPath, "local-data", "profiles.json"), "corrupt", TestContext.Current.CancellationToken);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.Profiles.SelectedProfile = ExtensionProfile.Create("Unlisted", [".txt"]);
        Assert.False(fixture.ViewModel.Profiles.CanWrite);
        Assert.True(fixture.ViewModel.ClearProfileCommand.CanExecute(null));
        Assert.False(fixture.ViewModel.SelectProfileCommand.CanExecute(fixture.ViewModel.Profiles.SelectedProfile));
        var reads = fixture.ProfileStore.LoadCount;
        fixture.ViewModel.ClearProfileCommand.Execute(null);
        Assert.Null(fixture.ViewModel.Profiles.SelectedProfile);
        Assert.Equal(reads, fixture.ProfileStore.LoadCount);
        Assert.Equal(0, fixture.ProfileStore.SaveCount);
        Assert.Equal("corrupt", await File.ReadAllTextAsync(Path.Combine(fixture.RootPath, "local-data", "profiles.json"), TestContext.Current.CancellationToken));
        Assert.Equal(0, fixture.FeatureDialogs.DeleteConfirmationCount);
    }

    [Fact]
    public async Task ClearedProfileCanBeAppliedAgainAfterManualSelectionChanges()
    {
        using var fixture = new MainWindowFixture();
        await ScanAsync(fixture, [".txt"]);
        var profile = Assert.Single(fixture.ViewModel.Profiles.Profiles);
        fixture.ViewModel.SelectProfileCommand.Execute(profile);
        fixture.ViewModel.ClearProfileCommand.Execute(null);
        fixture.ViewModel.Groups.SelectAll();
        fixture.ViewModel.SelectProfileCommand.Execute(profile);
        Assert.Same(profile, fixture.ViewModel.Profiles.SelectedProfile);
        Assert.Equal([".txt"], SelectedKeys(fixture));
        Assert.Equal(Format(fixture, "ProfileAppliedFormat"), fixture.ViewModel.StatusText);
        Assert.Equal(0, fixture.FeatureDialogs.DeleteConfirmationCount);
    }

    private static async Task ScanAsync(MainWindowFixture fixture, string[] keys)
    {
        var files = new[] { fixture.CreateScannedFile("file.txt"), fixture.CreateScannedFile("data.json"), fixture.CreateScannedFile("README") };
        fixture.Scanner.Handler = (_, _, _) => Task.FromResult(MainWindowFixture.Result(files));
        fixture.Picker.SourcePath = fixture.RootPath;
        await fixture.Profiles.SaveAsync([ExtensionProfile.Create("Text", keys)], TestContext.Current.CancellationToken);
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.ChooseFolderAsync();
    }

    private static string[] SelectedKeys(MainWindowFixture fixture) => fixture.ViewModel.Groups.AllRows
        .Where(row => row.IsSelected).Select(row => row.ExtensionKey).Order(StringComparer.Ordinal).ToArray();

    private static string Format(MainWindowFixture fixture, string key) => string.Format(fixture.Localization.CurrentCulture, fixture.Localization[key], "Text");
}
