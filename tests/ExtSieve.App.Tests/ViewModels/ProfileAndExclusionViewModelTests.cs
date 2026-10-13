using ExtSieve.App.LocalData;
using ExtSieve.App.Localization;
using ExtSieve.App.Settings;
using ExtSieve.App.Tests.Support;
using ExtSieve.App.ViewModels;
using ExtSieve.Core.Models;
using ExtSieve.Core.Services;

namespace ExtSieve.App.Tests.ViewModels;

public sealed class ProfileAndExclusionViewModelTests
{
    [Theory]
    [InlineData(".txt", 1)]
    [InlineData(".missing", 0)]
    [InlineData("", 1)]
    public void ProfileReplacesSelectionAcrossHiddenRowsAndIgnoresAbsentKeys(string key, int count)
    {
        var localization = new LocalizationService(LanguageMode.English);
        using var groups = new ExtensionGroupListViewModel(localization);
        groups.Load(new ScanResult([], [new(".txt", 1, 10), new(".json", 1, 20), new("", 1, 30)], [], TimeSpan.Zero));
        groups.SelectAll();
        groups.FilterText = "json";
        groups.ApplyProfile(ExtensionProfile.Create("Profile", [key, ".absent"]));
        Assert.Equal(count, groups.SelectedGroupCount);
        Assert.False(Assert.Single(groups.VisibleRows).IsSelected);
        Assert.Equal(count == 0 ? [] : new[] { key }, groups.AllRows.Where(row => row.IsSelected).Select(row => row.ExtensionKey));
    }

    [Fact]
    public void ProfileWithAllPresentKeysSelectsEveryGroup()
    {
        using var groups = new ExtensionGroupListViewModel(new LocalizationService());
        groups.Load(new ScanResult([], [new(".txt", 1, 10), new("", 1, 30)], [], TimeSpan.Zero));
        groups.ApplyProfile(ExtensionProfile.Create("All", ["", ".TXT"]));
        Assert.True(groups.SelectionState);
        Assert.Equal(40, groups.SelectedBytes);
    }

    [Fact]
    public async Task ManagerCreatesRenamesDuplicatesAndDeletesWithoutOverwritingDuplicateNames()
    {
        using var fixture = new MainWindowFixture();
        fixture.ViewModel.Groups.Load(new ScanResult([], [new(".txt", 1, 10)], [], TimeSpan.Zero));
        fixture.ViewModel.Groups.SelectAll();
        await fixture.ViewModel.InitializeAsync();
        fixture.FeatureDialogs.Name = "Docs";
        await fixture.ViewModel.SaveProfileAsync();
        var manager = fixture.ViewModel.Profiles;
        Assert.Equal("Docs", Assert.Single(manager.Profiles).Name);
        Assert.Equal("ProfileDuplicateName", manager.ValidateName(" docs "));
        fixture.FeatureDialogs.Name = "Text";
        await manager.RenameAsync();
        Assert.Equal("Text", Assert.Single(manager.Profiles).Name);
        fixture.FeatureDialogs.Name = "Copy";
        await manager.DuplicateAsync();
        Assert.Equal(["Copy", "Text"], manager.Profiles.Select(profile => profile.Name));
        Assert.Equal([".txt"], manager.SelectedProfile!.ExtensionKeys);
        fixture.FeatureDialogs.DeleteConfirmed = false;
        await manager.DeleteAsync();
        Assert.Equal(2, manager.Profiles.Count);
        fixture.FeatureDialogs.DeleteConfirmed = true;
        await manager.DeleteAsync();
        Assert.Equal("Text", Assert.Single(manager.Profiles).Name);
        Assert.Equal("Text", Assert.Single((await fixture.Profiles.LoadAsync(TestContext.Current.CancellationToken)).Value).Name);
    }

    [Fact]
    public async Task EmptySelectionAndCorruptStorageDisableProfileCreation()
    {
        using var fixture = new MainWindowFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.FeatureDialogs.Name = "Empty";
        await fixture.ViewModel.SaveProfileAsync();
        Assert.Empty(fixture.ViewModel.Profiles.Profiles);
        Assert.False(fixture.ViewModel.SaveProfileCommand.CanExecute(null));
        var path = Path.Combine(fixture.RootPath, "bad-profiles.json");
        await File.WriteAllTextAsync(path, "bad", TestContext.Current.CancellationToken);
        using var manager = new ProfileManagerViewModel(fixture.Localization, new JsonProfileStore(path), fixture.FeatureDialogs, () => [".txt"]);
        await manager.LoadAsync();
        Assert.True(manager.HasWarning);
        Assert.False(manager.CanWrite);
        await manager.CreateAsync();
        Assert.Empty(manager.Profiles);
    }

    [Fact]
    public async Task ApplyingExclusionsRescansUpdatesCountsAndProfilesDoNotAutoApply()
    {
        using var fixture = new MainWindowFixture();
        var kept = fixture.CreateScannedFile("kept/file.txt", [1, 2]);
        var excluded = fixture.CreateScannedFile("ignored/file.bin", [3, 4, 5]);
        fixture.Scanner.Handler = (_, _, _) => Task.FromResult(fixture.Scanner.LastExclusions!.Contains("ignored")
            ? MainWindowFixture.Result(kept) : MainWindowFixture.Result(kept, excluded));
        fixture.Picker.SourcePath = fixture.RootPath;
        await fixture.Profiles.SaveAsync([ExtensionProfile.Create("Text", [".txt"])], TestContext.Current.CancellationToken);
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.ChooseFolderAsync();
        fixture.ViewModel.SelectProfileCommand.Execute(Assert.Single(fixture.ViewModel.Profiles.Profiles));
        Assert.Equal(1, fixture.ViewModel.Groups.SelectedGroupCount);
        Assert.Equal(string.Format(fixture.Localization.CurrentCulture, fixture.Localization["ProfileAppliedFormat"], "Text"), fixture.ViewModel.StatusText);
        fixture.FeatureDialogs.EditHandler = editor => { editor.SetExcluded("ignored", true); return Task.FromResult(true); };
        await fixture.ViewModel.EditExclusionsAsync();
        Assert.Equal(2, fixture.Scanner.CallCount);
        Assert.Equal("1", fixture.ViewModel.TotalFilesDisplay);
        Assert.Equal("1", fixture.ViewModel.TotalGroupsDisplay);
        Assert.Equal("2 B", fixture.ViewModel.TotalBytesDisplay);
        Assert.Equal(0, fixture.ViewModel.Groups.SelectedGroupCount);
        Assert.Null(fixture.ViewModel.Profiles.SelectedProfile);
        Assert.Equal(fixture.Localization["StatusResults"], fixture.ViewModel.StatusText);
        Assert.Equal(["ignored"], (await fixture.Exclusions.LoadAsync(fixture.RootPath, TestContext.Current.CancellationToken)).Value.Paths);
        await fixture.ViewModel.ScanAgainAsync();
        Assert.Equal(["ignored"], fixture.Scanner.LastExclusions!.Paths);
    }

    [Fact]
    public async Task CancellingExclusionDialogPreservesScanAndConfiguration()
    {
        using var fixture = new MainWindowFixture();
        fixture.Picker.SourcePath = fixture.RootPath;
        fixture.Scanner.Handler = (_, _, _) => Task.FromResult(MainWindowFixture.Result(fixture.CreateScannedFile("file.txt")));
        await fixture.ViewModel.ChooseFolderAsync();
        fixture.ViewModel.Groups.SelectAll();
        fixture.FeatureDialogs.EditHandler = editor => { editor.SetExcluded("ignored", true); return Task.FromResult(false); };
        await fixture.ViewModel.EditExclusionsAsync();
        Assert.Equal(1, fixture.Scanner.CallCount);
        Assert.False(fixture.ViewModel.HasExclusions);
        Assert.True(fixture.ViewModel.Groups.SelectionState);
    }

    [Fact]
    public async Task ExclusionSaveFailureStillInvalidatesManifestAndAppliesTheSessionConfiguration()
    {
        using var fixture = new MainWindowFixture();
        fixture.Picker.SourcePath = fixture.RootPath;
        await fixture.ViewModel.ChooseFolderAsync();
        Directory.CreateDirectory(Path.Combine(fixture.RootPath, "local-data"));
        await File.WriteAllTextAsync(Path.Combine(fixture.RootPath, "local-data", "exclusions.json"), "corrupt", TestContext.Current.CancellationToken);
        fixture.FeatureDialogs.EditHandler = editor => { editor.SetExcluded("ignored", true); return Task.FromResult(true); };
        await fixture.ViewModel.EditExclusionsAsync();
        Assert.Equal(2, fixture.Scanner.CallCount);
        Assert.True(fixture.ViewModel.HasExclusions);
        Assert.True(fixture.ViewModel.HasFeatureWarning);
        Assert.Equal(fixture.Localization["ExclusionsSaveWarning"], fixture.ViewModel.FeatureWarning);
        Assert.Equal(["ignored"], fixture.Scanner.LastExclusions!.Paths);
        Assert.Equal(string.Format(fixture.Localization.CurrentCulture, fixture.Localization["ExcludedFolderSingularFormat"], 1),
            fixture.ViewModel.SelectionSummary.Split(" · ")[3]);
        await fixture.ViewModel.ScanAgainAsync();
        Assert.Equal(["ignored"], fixture.Scanner.LastExclusions!.Paths);
    }

    [Fact]
    public async Task ScanInProgressPreventsConflictingProfileExclusionAndRefreshCommands()
    {
        using var fixture = new MainWindowFixture();
        fixture.Picker.SourcePath = fixture.RootPath;
        fixture.Scanner.Handler = async (_, _, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return MainWindowFixture.Result();
        };
        var scan = fixture.ViewModel.ChooseFolderAsync();
        Assert.True(fixture.ViewModel.IsScanning);
        Assert.False(fixture.ViewModel.EditExclusionsCommand.CanExecute(null));
        Assert.False(fixture.ViewModel.ManageProfilesCommand.CanExecute(null));
        Assert.False(fixture.ViewModel.ScanAgainCommand.CanExecute(null));
        await fixture.ViewModel.EditExclusionsAsync();
        await fixture.ViewModel.ScanAgainAsync();
        Assert.Equal(1, fixture.Scanner.CallCount);
        fixture.ViewModel.CancelActiveOperation();
        await scan;
        Assert.Equal(MainWindowState.Cancelled, fixture.ViewModel.State);
    }

    [Fact]
    public async Task ProfileSaveFailurePreservesTheInMemoryLibrary()
    {
        using var fixture = new MainWindowFixture();
        var path = Path.Combine(fixture.RootPath, "library.json");
        var store = new JsonProfileStore(path);
        await store.SaveAsync([ExtensionProfile.Create("Original", [".txt"])], TestContext.Current.CancellationToken);
        using var manager = new ProfileManagerViewModel(fixture.Localization, store, fixture.FeatureDialogs, () => [".pdf"]);
        await manager.LoadAsync();
        await File.WriteAllTextAsync(path, "corrupt", TestContext.Current.CancellationToken);
        fixture.FeatureDialogs.Name = "New";
        await manager.CreateAsync();
        Assert.Equal("Original", Assert.Single(manager.Profiles).Name);
        Assert.True(manager.HasWarning);
        Assert.Equal("corrupt", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SearchCancellationKeepsTheEditorUsable()
    {
        using var fixture = new MainWindowFixture();
        using var editor = new FolderExclusionsViewModel(fixture.Localization, fixture.RootPath, new FolderExclusionSet([]), new WaitingBrowser());
        editor.SearchText = "folder";
        var search = editor.SearchAsync();
        Assert.True(editor.IsSearching);
        Assert.False(editor.CanApply);
        editor.CancelSearchCommand.Execute(null);
        await search;
        Assert.False(editor.IsSearching);
        Assert.True(editor.CanApply);
    }

    private sealed class WaitingBrowser : IFolderBrowser
    {
        public async Task<FolderChildren> GetChildrenAsync(string root, string relativeDirectory, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new FolderChildren([], false);
        }
    }

    [Fact]
    public async Task RootChangeRestoresItsOwnExclusions()
    {
        using var fixture = new MainWindowFixture();
        var second = Path.Combine(fixture.RootPath, "second");
        Directory.CreateDirectory(second);
        await fixture.Exclusions.SaveAsync(fixture.RootPath, new FolderExclusionSet(["ignored"]), TestContext.Current.CancellationToken);
        fixture.Picker.SourcePath = fixture.RootPath;
        await fixture.ViewModel.ChooseFolderAsync();
        Assert.Equal(["ignored"], fixture.Scanner.LastExclusions!.Paths);
        fixture.Picker.SourcePath = second;
        await fixture.ViewModel.ChooseFolderAsync();
        Assert.Empty(fixture.Scanner.LastExclusions!.Paths);
        fixture.Picker.SourcePath = fixture.RootPath;
        await fixture.ViewModel.ChooseFolderAsync();
        Assert.Equal(["ignored"], fixture.Scanner.LastExclusions!.Paths);
    }

    [Fact]
    public async Task EditorCompactsParentChildSupportsRemovalResetAndSearch()
    {
        using var fixture = new MainWindowFixture();
        fixture.CreateScannedFile("src/obj/file.txt");
        fixture.CreateScannedFile("assets/temp/file.txt");
        using var editor = new FolderExclusionsViewModel(fixture.Localization, fixture.RootPath,
            new FolderExclusionSet(["assets/temp"]), new FolderBrowser());
        await editor.InitializeAsync();
        editor.SetExcluded("src/obj", true);
        editor.SetExcluded("src", true);
        Assert.Equal(["assets/temp", "src"], editor.Paths);
        Assert.True(editor.RootNodes.Single(node => node.RelativePath == "src").IsExcluded);
        editor.ResetCommand.Execute(null);
        Assert.Equal(["assets/temp"], editor.Paths);
        editor.SearchText = "obj";
        await editor.SearchAsync();
        Assert.Equal("src/obj", Assert.Single(editor.VisibleNodes).RelativePath);
        editor.VisibleNodes[0].IsExcluded = true;
        editor.ShowOnlyExcluded = true;
        editor.VisibleNodes[0].IsExcluded = false;
        Assert.Equal(["assets/temp"], editor.Paths);
        editor.SearchText = "";
        Assert.Single(editor.VisibleNodes).IsExcluded = false;
        Assert.Empty(editor.Paths);
    }

    [Theory]
    [InlineData(LanguageMode.English)]
    [InlineData(LanguageMode.Italian)]
    [InlineData(LanguageMode.German)]
    [InlineData(LanguageMode.French)]
    [InlineData(LanguageMode.Spanish)]
    [InlineData(LanguageMode.PortugueseBrazil)]
    public void DialogLabelsAndValidationFollowTheCurrentLanguage(LanguageMode mode)
    {
        using var fixture = new MainWindowFixture();
        using var input = new ProfileNameViewModel(fixture.Localization, "SaveProfile", [""], _ => "ProfileInvalidName");
        fixture.Localization.SetLanguage(mode);
        Assert.Equal(fixture.Localization["SaveProfile"], input.Title);
        Assert.Equal(fixture.Localization["ProfileInvalidName"], input.Error);
        Assert.Equal(fixture.Localization["NoExtension"], input.ExtensionDisplay);
    }
}
