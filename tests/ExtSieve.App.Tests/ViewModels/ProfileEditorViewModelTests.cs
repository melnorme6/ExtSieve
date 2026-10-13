using ExtSieve.App.LocalData;
using ExtSieve.App.Localization;
using ExtSieve.App.Settings;
using ExtSieve.App.Tests.Support;
using ExtSieve.App.ViewModels;
using ExtSieve.Core.Models;

namespace ExtSieve.App.Tests.ViewModels;

public sealed class ProfileEditorViewModelTests
{
    [Fact]
    public async Task NewCreatesSelectedEmptyProfileIndependentOfScanAndSelection()
    {
        using var fixture = new MainWindowFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Picker.SourcePath = fixture.RootPath;
        fixture.Scanner.Handler = (_, _, _) => Task.FromResult(MainWindowFixture.Result(fixture.CreateScannedFile("file.txt")));
        await fixture.ViewModel.ChooseFolderAsync();
        fixture.ViewModel.Groups.SelectAll();
        fixture.FeatureDialogs.Name = "  Empty  ";
        var manager = fixture.ViewModel.Profiles;
        await manager.CreateAsync();
        var profile = Assert.Single(manager.Profiles);
        Assert.Equal("Empty", profile.Name);
        Assert.Empty(profile.ExtensionKeys);
        Assert.Same(profile, manager.SelectedProfile);
        Assert.True(Assert.Single(manager.ExtensionItems).IsAdd);
        Assert.True(manager.AddExtensionCommand.CanExecute(null));
        Assert.Empty(Assert.Single((await fixture.Profiles.LoadAsync(TestContext.Current.CancellationToken)).Value).ExtensionKeys);
        Assert.Equal([".txt"], fixture.ViewModel.Groups.AllRows.Where(row => row.IsSelected).Select(row => row.ExtensionKey));
        fixture.ViewModel.SelectProfile(profile);
        Assert.Equal(0, fixture.ViewModel.Groups.SelectedGroupCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(" docs ")]
    public async Task InvalidDuplicateOrCancelledNamesPreserveLibrary(string? name)
    {
        using var fixture = new MainWindowFixture();
        await fixture.Profiles.SaveAsync([ExtensionProfile.Create("Docs", [".txt"])], TestContext.Current.CancellationToken);
        await fixture.ViewModel.InitializeAsync();
        var path = Path.Combine(fixture.RootPath, "local-data", "profiles.json");
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        fixture.FeatureDialogs.Name = name;
        await fixture.ViewModel.Profiles.CreateAsync();
        Assert.Single(fixture.ViewModel.Profiles.Profiles);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DuplicateHasIndependentSortedKeysAndRemovalCanLeaveEmptyProfile()
    {
        using var fixture = new MainWindowFixture();
        await fixture.Profiles.SaveAsync([ExtensionProfile.Create("Original", [".TXT", "", ".cs"])], TestContext.Current.CancellationToken);
        await fixture.ViewModel.InitializeAsync();
        var manager = fixture.ViewModel.Profiles;
        manager.SelectedProfile = Assert.Single(manager.Profiles);
        fixture.FeatureDialogs.Name = "Copy";
        await manager.DuplicateAsync();
        Assert.Equal("Copy", manager.SelectedProfile!.Name);
        Assert.Equal(["", ".cs", ".txt"], manager.SelectedProfile.ExtensionKeys);
        foreach (var key in manager.SelectedProfile.ExtensionKeys.ToArray()) await manager.RemoveExtensionAsync(key);
        Assert.Empty(manager.SelectedProfile.ExtensionKeys);
        Assert.Equal(["", ".cs", ".txt"], manager.Profiles.Single(profile => profile.Name == "Original").ExtensionKeys);
        Assert.Contains("0", manager.ExtensionSummary, StringComparison.Ordinal);
        Assert.True(Assert.Single(manager.ExtensionItems).IsAdd);
        var saved = (await fixture.Profiles.LoadAsync(TestContext.Current.CancellationToken)).Value;
        Assert.Empty(saved.Single(profile => profile.Name == "Copy").ExtensionKeys);
        fixture.FeatureDialogs.Name = "Renamed empty";
        await manager.RenameAsync();
        Assert.Equal("Renamed empty", manager.SelectedProfile.Name);
        fixture.FeatureDialogs.Name = "Empty copy";
        await manager.DuplicateAsync();
        Assert.Empty(manager.SelectedProfile.ExtensionKeys);
    }

    [Fact]
    public async Task CustomExtensionUpdatesPillsCountAndStorageWithoutApplyingSelection()
    {
        using var fixture = new MainWindowFixture();
        await fixture.ViewModel.InitializeAsync();
        var manager = fixture.ViewModel.Profiles;
        fixture.FeatureDialogs.Name = "Custom";
        await manager.CreateAsync();
        fixture.FeatureDialogs.ExtensionHandler = input => { input.Input = " PSD "; return Task.FromResult(input.NormalizedKey); };
        await manager.AddExtensionAsync();
        Assert.Equal([".psd"], manager.SelectedProfile!.ExtensionKeys);
        Assert.Equal(2, manager.ExtensionItems.Count);
        Assert.False(manager.ExtensionItems[0].IsAdd);
        Assert.True(manager.ExtensionItems[^1].IsAdd);
        Assert.Equal([".psd"], Assert.Single((await fixture.Profiles.LoadAsync(TestContext.Current.CancellationToken)).Value).ExtensionKeys);
        Assert.Empty(fixture.ViewModel.Groups.AllRows);
        fixture.FeatureDialogs.ExtensionHandler = input => { input.Input = ".PSD"; return Task.FromResult(input.NormalizedKey); };
        await manager.AddExtensionAsync();
        Assert.Single(manager.SelectedProfile.ExtensionKeys);
        Assert.True(manager.HasWarning);
        fixture.FeatureDialogs.ExtensionHandler = _ => Task.FromResult<string?>(null);
        await manager.AddExtensionAsync();
        Assert.Single(manager.SelectedProfile.ExtensionKeys);
    }

    [Theory]
    [InlineData("cs", ".cs")]
    [InlineData(" .Cs ", ".cs")]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData(".", null)]
    [InlineData("../cs", null)]
    [InlineData(".a.b", null)]
    [InlineData("c:s", null)]
    public void ExtensionInputUsesCanonicalNormalizationAndValidation(string input, string? expected)
    {
        using var editor = new ProfileExtensionViewModel(new LocalizationService(), []);
        editor.Input = input;
        Assert.Equal(expected, editor.NormalizedKey);
        Assert.Equal(expected is not null, editor.CanAdd);
    }

    [Fact]
    public void ExtensionlessInputAndCanonicalDuplicateAreExplicit()
    {
        using var editor = new ProfileExtensionViewModel(new LocalizationService(), [".cs"]);
        editor.Input = " CS ";
        Assert.False(editor.CanAdd);
        editor.Extensionless = true;
        Assert.True(editor.CanAdd);
        Assert.Equal("", editor.NormalizedKey);
        Assert.False(editor.CanEnterKey);
        using var duplicate = new ProfileExtensionViewModel(new LocalizationService(), [""]);
        duplicate.Extensionless = true;
        Assert.False(duplicate.CanAdd);
    }

    [Fact]
    public async Task NoSelectionDisablesEditingAndExportButNotNewAndImport()
    {
        using var fixture = new MainWindowFixture();
        await fixture.ViewModel.InitializeAsync();
        var manager = fixture.ViewModel.Profiles;
        Assert.True(manager.CreateCommand.CanExecute(null));
        Assert.True(manager.ImportCommand.CanExecute(null));
        Assert.False(manager.RenameCommand.CanExecute(null));
        Assert.False(manager.DuplicateCommand.CanExecute(null));
        Assert.False(manager.DeleteCommand.CanExecute(null));
        Assert.False(manager.ExportCommand.CanExecute(null));
        Assert.False(manager.AddExtensionCommand.CanExecute(null));
        Assert.True(Assert.Single(manager.ExtensionItems).IsAdd);
        Assert.False(Assert.Single(manager.ExtensionItems).Command.CanExecute(null));
        await manager.ExportAsync();
        Assert.Null(fixture.FeatureDialogs.ExportedProfile);
    }

    [Fact]
    public async Task ImportSkipsCaseInsensitiveConflictsAndExportsOnlySelectedProfile()
    {
        using var fixture = new MainWindowFixture();
        await fixture.Profiles.SaveAsync([ExtensionProfile.Create("Docs", [".txt"])], TestContext.Current.CancellationToken);
        await fixture.ViewModel.InitializeAsync();
        fixture.FeatureDialogs.ImportHandler = () => Task.FromResult<IReadOnlyList<ExtensionProfile>?>([
            ExtensionProfile.Create(" docs ", [".pdf"]), ExtensionProfile.Create("Unity", [".cs", ".asset"])]);
        var manager = fixture.ViewModel.Profiles;
        await manager.ImportAsync();
        Assert.Equal(2, manager.Profiles.Count);
        Assert.Equal([".txt"], manager.Profiles.Single(profile => profile.Name == "Docs").ExtensionKeys);
        Assert.Equal("Unity", manager.SelectedProfile!.Name);
        Assert.Equal(string.Format(fixture.Localization.CurrentCulture, fixture.Localization["ProfileImportSummaryFormat"], 1, 1), manager.Message);
        await manager.ExportAsync();
        Assert.Same(manager.SelectedProfile, fixture.FeatureDialogs.ExportedProfile);
        Assert.Equal(2, (await fixture.Profiles.LoadAsync(TestContext.Current.CancellationToken)).Value.Count);
        Assert.Empty(fixture.ViewModel.Groups.AllRows);
    }

    [Fact]
    public async Task ImportFailureCancelAndAllConflictsPreserveSelectionAndDisk()
    {
        using var fixture = new MainWindowFixture();
        await fixture.Profiles.SaveAsync([ExtensionProfile.Create("Docs", [".txt"])], TestContext.Current.CancellationToken);
        await fixture.ViewModel.InitializeAsync();
        var manager = fixture.ViewModel.Profiles;
        manager.SelectedProfile = Assert.Single(manager.Profiles);
        var before = manager.SelectedProfile;
        var path = Path.Combine(fixture.RootPath, "local-data", "profiles.json");
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        await manager.ImportAsync();
        fixture.FeatureDialogs.ImportHandler = () => Task.FromException<IReadOnlyList<ExtensionProfile>?>(new InvalidDataException("Invalid document."));
        await manager.ImportAsync();
        Assert.True(manager.HasWarning);
        fixture.FeatureDialogs.ImportHandler = () => Task.FromResult<IReadOnlyList<ExtensionProfile>?>([ExtensionProfile.Create("DOCS", [])]);
        await manager.ImportAsync();
        Assert.Same(before, manager.SelectedProfile);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(string.Format(fixture.Localization.CurrentCulture, fixture.Localization["ProfileImportSummaryFormat"], 0, 1), manager.Message);
    }

    [Fact]
    public async Task FailedExtensionAndImportSavesPreserveMemoryAndSelection()
    {
        using var fixture = new MainWindowFixture();
        var store = new FailingStore([ExtensionProfile.Create("Docs", [".txt"])]);
        using var manager = new ProfileManagerViewModel(fixture.Localization, store, fixture.FeatureDialogs, () => []);
        await manager.LoadAsync();
        manager.SelectedProfile = Assert.Single(manager.Profiles);
        var original = manager.SelectedProfile;
        await manager.RemoveExtensionAsync(".txt");
        Assert.Same(original, manager.SelectedProfile);
        Assert.Equal([".txt"], original.ExtensionKeys);
        fixture.FeatureDialogs.ImportHandler = () => Task.FromResult<IReadOnlyList<ExtensionProfile>?>([ExtensionProfile.Create("Other", [])]);
        await manager.ImportAsync();
        Assert.Same(original, Assert.Single(manager.Profiles));
        Assert.True(manager.HasWarning);
        Assert.Equal(2, store.SaveAttempts);
    }

    [Fact]
    public async Task BusyDialogDisablesConflictingActionsAndDeleteSelectsNeighbor()
    {
        using var fixture = new MainWindowFixture();
        await fixture.Profiles.SaveAsync([ExtensionProfile.Create("A", []), ExtensionProfile.Create("B", [])], TestContext.Current.CancellationToken);
        await fixture.ViewModel.InitializeAsync();
        var manager = fixture.ViewModel.Profiles;
        manager.SelectedProfile = manager.Profiles[0];
        var pending = new TaskCompletionSource<string?>();
        fixture.FeatureDialogs.ExtensionHandler = _ => pending.Task;
        var operation = manager.AddExtensionAsync();
        Assert.False(manager.CanWrite);
        Assert.False(manager.CreateCommand.CanExecute(null));
        Assert.False(manager.ImportCommand.CanExecute(null));
        Assert.False(manager.ExportCommand.CanExecute(null));
        await manager.DeleteAsync();
        Assert.Equal(2, manager.Profiles.Count);
        pending.SetResult(null);
        await operation;
        fixture.FeatureDialogs.DeleteConfirmed = false;
        await manager.DeleteAsync();
        Assert.Equal("A", manager.SelectedProfile!.Name);
        fixture.FeatureDialogs.DeleteConfirmed = true;
        await manager.DeleteAsync();
        Assert.Equal("B", manager.SelectedProfile!.Name);
        await manager.DeleteAsync();
        Assert.Null(manager.SelectedProfile);
    }

    [Theory]
    [InlineData(LanguageMode.English)]
    [InlineData(LanguageMode.Italian)]
    [InlineData(LanguageMode.German)]
    [InlineData(LanguageMode.French)]
    [InlineData(LanguageMode.Spanish)]
    [InlineData(LanguageMode.PortugueseBrazil)]
    public void EditorLabelsAndValidationAreLocalized(LanguageMode language)
    {
        var localization = new LocalizationService(language);
        using var editor = new ProfileExtensionViewModel(localization, [".cs"]);
        using var name = new ProfileNameViewModel(localization, "NewProfile", [], _ => null, allowEmpty: true);
        Assert.Equal(localization["AddProfileExtension"], editor.Title);
        Assert.Equal(localization["ProfileExtensionInvalid"], editor.Error);
        editor.Input = "cs";
        Assert.Equal(localization["ProfileExtensionDuplicate"], editor.Error);
        Assert.Equal(localization["ProfileEmptyHelp"], name.Help);
        Assert.True(name.CanSave);
    }

    [Fact]
    public async Task ExportCancelAndFailurePreserveSelectionLibraryAndStorage()
    {
        using var fixture = new MainWindowFixture();
        await fixture.Profiles.SaveAsync([ExtensionProfile.Create("Docs", [".txt"])], TestContext.Current.CancellationToken);
        await fixture.ViewModel.InitializeAsync();
        var manager = fixture.ViewModel.Profiles;
        manager.SelectedProfile = Assert.Single(manager.Profiles);
        var original = manager.SelectedProfile;
        var path = Path.Combine(fixture.RootPath, "local-data", "profiles.json");
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        fixture.FeatureDialogs.ExportHandler = _ => Task.FromResult(false);
        await manager.ExportAsync();
        Assert.False(manager.HasMessage);
        fixture.FeatureDialogs.ExportHandler = _ => Task.FromException<bool>(new IOException("Destination unavailable."));
        await manager.ExportAsync();
        Assert.True(manager.HasWarning);
        Assert.Same(original, manager.SelectedProfile);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CorruptStoragePreservesActualBytesAndLastPublishedPillsOnEdit()
    {
        using var fixture = new MainWindowFixture();
        await fixture.Profiles.SaveAsync([ExtensionProfile.Create("Docs", [".txt"])], TestContext.Current.CancellationToken);
        await fixture.ViewModel.InitializeAsync();
        var manager = fixture.ViewModel.Profiles;
        manager.SelectedProfile = Assert.Single(manager.Profiles);
        var original = manager.SelectedProfile;
        var path = Path.Combine(fixture.RootPath, "local-data", "profiles.json");
        await File.WriteAllTextAsync(path, "corrupt", TestContext.Current.CancellationToken);
        await manager.RemoveExtensionAsync(".txt");
        Assert.Same(original, manager.SelectedProfile);
        Assert.Equal([".txt"], manager.ExtensionItems.Where(item => !item.IsAdd).Select(item => item.Key));
        Assert.Equal("corrupt", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.True(manager.HasWarning);
    }

    private sealed class FailingStore(IReadOnlyList<ExtensionProfile> profiles) : IProfileStore
    {
        public int SaveAttempts { get; private set; }
        public Task<LocalDataResult<IReadOnlyList<ExtensionProfile>>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new LocalDataResult<IReadOnlyList<ExtensionProfile>>(profiles, true));
        public Task SaveAsync(IReadOnlyList<ExtensionProfile> value, CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            return Task.FromException(new IOException("Storage unavailable."));
        }
    }
}
