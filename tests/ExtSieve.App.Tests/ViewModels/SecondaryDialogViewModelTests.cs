using ExtSieve.App.Localization;
using ExtSieve.App.Settings;
using ExtSieve.App.Tests.Support;
using ExtSieve.App.ViewModels;
using ExtSieve.Core.Models;
using ExtSieve.Core.Services;

namespace ExtSieve.App.Tests.ViewModels;

public sealed class SecondaryDialogViewModelTests
{
    [Theory]
    [InlineData(LanguageMode.English)]
    [InlineData(LanguageMode.Italian)]
    [InlineData(LanguageMode.German)]
    [InlineData(LanguageMode.French)]
    [InlineData(LanguageMode.Spanish)]
    [InlineData(LanguageMode.PortugueseBrazil)]
    public async Task DuplicateProposesFirstAvailableLocalizedNameWithoutInitialError(LanguageMode language)
    {
        using var fixture = new MainWindowFixture();
        fixture.Localization.SetLanguage(language);
        string Name(int index, string original = "Unity") => string.Format(fixture.Localization.CurrentCulture,
            fixture.Localization[index == 1 ? "ProfileCopyNameFormat" : "ProfileCopyNumberedNameFormat"], original, index);
        await fixture.Profiles.SaveAsync([
            ExtensionProfile.Create("Unity", ["", ".cs", ".asset"]),
            ExtensionProfile.Create(Name(1).ToUpperInvariant(), []),
            ExtensionProfile.Create(Name(2).ToUpperInvariant(), []),
            ExtensionProfile.Create(Name(4), [])], TestContext.Current.CancellationToken);
        await fixture.ViewModel.InitializeAsync();
        fixture.Localization.SetLanguage(language);
        var manager = fixture.ViewModel.Profiles;
        manager.SelectedProfile = manager.Profiles.Single(profile => profile.Name == "Unity");
        fixture.FeatureDialogs.NameHandler = input =>
        {
            Assert.Equal(Name(3), input.Name);
            Assert.Empty(input.Error);
            Assert.True(input.CanSave);
            Assert.True(input.SelectInitialName);
            Assert.False(input.ShowExtensions);
            Assert.Equal(fixture.Localization["NewProfileName"], input.NameLabel);
            Assert.Contains("Unity", input.Help, StringComparison.Ordinal);
            return Task.FromResult<string?>(input.Name);
        };
        await manager.DuplicateAsync();
        Assert.Equal(Name(3), manager.SelectedProfile!.Name);
        Assert.Equal(["", ".asset", ".cs"], manager.SelectedProfile.ExtensionKeys);
        Assert.Equal(5, (await fixture.Profiles.LoadAsync(TestContext.Current.CancellationToken)).Value.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuggestedCopyNamesRespectLengthAndDoNotSplitUnicodePairs(bool unicode)
    {
        using var fixture = new MainWindowFixture();
        var source = unicode ? string.Concat(Enumerable.Repeat("\U0001F642", 40)) : new string('x', 80);
        await fixture.Profiles.SaveAsync([ExtensionProfile.Create(source, [])], TestContext.Current.CancellationToken);
        await fixture.ViewModel.InitializeAsync();
        var manager = fixture.ViewModel.Profiles;
        manager.SelectedProfile = Assert.Single(manager.Profiles);
        fixture.FeatureDialogs.NameHandler = input =>
        {
            Assert.InRange(input.Name.Length, 1, 80);
            Assert.True(input.CanSave);
            Assert.Empty(input.Error);
            for (var index = 0; index < input.Name.Length; index++)
                if (char.IsHighSurrogate(input.Name[index])) Assert.True(char.IsLowSurrogate(input.Name[++index]));
            return Task.FromResult<string?>(input.Name);
        };
        await manager.DuplicateAsync();
        Assert.Equal(2, manager.Profiles.Count);
        Assert.Empty(manager.SelectedProfile!.ExtensionKeys);
        Assert.Equal(source, manager.Profiles.Single(profile => profile.Name == source).Name);
    }

    [Fact]
    public async Task CancellingSuggestedDuplicatePreservesSelectionAndDisk()
    {
        using var fixture = new MainWindowFixture();
        await fixture.Profiles.SaveAsync([ExtensionProfile.Create("Unity", [".cs"])], TestContext.Current.CancellationToken);
        await fixture.ViewModel.InitializeAsync();
        var manager = fixture.ViewModel.Profiles;
        manager.SelectedProfile = Assert.Single(manager.Profiles);
        var selected = manager.SelectedProfile;
        var path = Path.Combine(fixture.RootPath, "local-data", "profiles.json");
        var original = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        fixture.FeatureDialogs.NameHandler = input =>
        {
            Assert.Equal(string.Format(fixture.Localization.CurrentCulture, fixture.Localization["ProfileCopyNameFormat"], "Unity"), input.Name);
            Assert.True(input.CanSave);
            return Task.FromResult<string?>(null);
        };
        await manager.DuplicateAsync();
        Assert.Same(selected, manager.SelectedProfile);
        Assert.Single(manager.Profiles);
        Assert.Equal(original, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NewAndRenameHaveOperationSpecificContentAndRetainNameValidation()
    {
        using var fixture = new MainWindowFixture();
        await fixture.ViewModel.InitializeAsync();
        var manager = fixture.ViewModel.Profiles;
        fixture.FeatureDialogs.NameHandler = input =>
        {
            Assert.Equal(fixture.Localization["NewProfile"], input.Title);
            Assert.False(input.ShowExtensions);
            Assert.Equal(fixture.Localization["ProfileEmptyHelp"], input.Help);
            Assert.False(input.CanSave);
            input.Name = "  ";
            Assert.False(input.CanSave);
            input.Name = "Unity";
            Assert.True(input.CanSave);
            return Task.FromResult<string?>(input.Name);
        };
        await manager.CreateAsync();
        Assert.Empty(manager.SelectedProfile!.ExtensionKeys);
        fixture.FeatureDialogs.ExtensionHandler = _ => Task.FromResult<string?>(".cs");
        await manager.AddExtensionAsync();
        fixture.FeatureDialogs.NameHandler = input =>
        {
            Assert.Equal("Unity", input.Name);
            Assert.False(input.ShowExtensions);
            Assert.False(input.HasHelp);
            Assert.Empty(input.Help);
            input.Name = "";
            Assert.False(input.CanSave);
            input.Name = "New name";
            return Task.FromResult<string?>(input.Name);
        };
        await manager.RenameAsync();
        Assert.Equal("New name", manager.SelectedProfile.Name);
        Assert.Equal([".cs"], manager.SelectedProfile.ExtensionKeys);
        Assert.Equal([".cs"], Assert.Single((await fixture.Profiles.LoadAsync(TestContext.Current.CancellationToken)).Value).ExtensionKeys);
    }

    [Fact]
    public async Task MainSelectionSavingRetainsItsExtensionPreview()
    {
        using var fixture = new MainWindowFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.Groups.Load(new ScanResult([], [new(".cs", 1, 1), new("", 1, 1)], [], TimeSpan.Zero));
        fixture.ViewModel.Groups.SelectAll();
        fixture.FeatureDialogs.NameHandler = input =>
        {
            Assert.True(input.ShowExtensions);
            Assert.Contains(fixture.Localization["NoExtension"], input.ExtensionDisplay, StringComparison.Ordinal);
            Assert.Equal(fixture.Localization["ProfileSaveHelp"], input.Help);
            input.Name = "Selection";
            return Task.FromResult<string?>(input.Name);
        };
        await fixture.ViewModel.SaveProfileAsync();
        Assert.Equal(["", ".cs"], Assert.Single(fixture.ViewModel.Profiles.Profiles).ExtensionKeys);
    }

    [Fact]
    public async Task ExplicitExtensionlessChoiceStillPersistsAndAppliesTheSpecialGroup()
    {
        using var fixture = new MainWindowFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Picker.SourcePath = fixture.RootPath;
        fixture.Scanner.Handler = (_, _, _) => Task.FromResult(MainWindowFixture.Result(fixture.CreateScannedFile("README")));
        await fixture.ViewModel.ChooseFolderAsync();
        fixture.FeatureDialogs.Name = "Extensionless";
        var manager = fixture.ViewModel.Profiles;
        await manager.CreateAsync();
        fixture.FeatureDialogs.ExtensionHandler = input =>
        {
            input.Extensionless = true;
            Assert.True(input.CanAdd);
            Assert.False(input.CanEnterKey);
            Assert.Equal("", input.NormalizedKey);
            return Task.FromResult(input.NormalizedKey);
        };
        await manager.AddExtensionAsync();
        Assert.Equal([""], manager.SelectedProfile!.ExtensionKeys);
        Assert.Equal([""], Assert.Single((await fixture.Profiles.LoadAsync(TestContext.Current.CancellationToken)).Value).ExtensionKeys);
        fixture.ViewModel.SelectProfile(manager.SelectedProfile);
        Assert.True(Assert.Single(fixture.ViewModel.Groups.AllRows).IsSelected);
    }

    [Fact]
    public async Task OnlyExcludedUsesFullConfiguredPathsWithoutBrowsingOrChangingTheDraft()
    {
        using var fixture = new MainWindowFixture();
        fixture.CreateScannedFile("assets/temp/file.txt");
        fixture.CreateScannedFile("src/obj/file.txt");
        var browser = new CountingBrowser();
        var initial = new FolderExclusionSet(["assets/temp", "missing/deep/cache"]);
        using var editor = new FolderExclusionsViewModel(fixture.Localization, fixture.RootPath, initial, browser);
        await editor.InitializeAsync();
        Assert.False(editor.ShowOnlyExcluded);
        Assert.Equal(["assets", "src"], editor.VisibleNodes.Select(node => node.RelativePath));
        var roots = editor.RootNodes;
        var calls = browser.Calls;
        editor.ShowOnlyExcluded = true;
        Assert.Equal(initial.Paths, editor.VisibleNodes.Select(node => node.RelativePath));
        Assert.All(editor.VisibleNodes, node => { Assert.True(node.IsExcluded); Assert.True(node.CanToggle); Assert.Empty(node.Children); Assert.Equal(node.RelativePath, node.DisplayName); });
        Assert.Same(initial, editor.Exclusions);
        Assert.Equal(calls, browser.Calls);
        editor.VisibleNodes.Single(node => node.RelativePath == "missing/deep/cache").IsExcluded = false;
        Assert.Equal(["assets/temp"], editor.Paths);
        editor.ShowOnlyExcluded = false;
        Assert.Same(roots, editor.VisibleNodes);
        editor.ResetCommand.Execute(null);
        Assert.Same(initial, editor.Exclusions);
    }

    [Fact]
    public async Task FilterComposesWithSearchAndRestoresItsResultsAndBrowsedTree()
    {
        using var fixture = new MainWindowFixture();
        fixture.CreateScannedFile("Assets/cache/file.txt");
        fixture.CreateScannedFile("Assets/other/file.txt");
        fixture.CreateScannedFile("src/cache/file.txt");
        using var editor = new FolderExclusionsViewModel(fixture.Localization, fixture.RootPath,
            new FolderExclusionSet(["Assets/cache", "missing/cache", "other"]), new FolderBrowser());
        await editor.InitializeAsync();
        editor.SearchText = " CACHE ";
        await editor.SearchAsync();
        var normalMatches = editor.VisibleNodes;
        Assert.Equal(["Assets/cache", "src/cache"], normalMatches.Select(node => node.RelativePath).Order(StringComparer.Ordinal));
        editor.ShowOnlyExcluded = true;
        Assert.Equal(["Assets/cache", "missing/cache"], editor.VisibleNodes.Select(node => node.RelativePath));
        Assert.Contains("3", editor.CountLabel, StringComparison.Ordinal);
        editor.ShowOnlyExcluded = false;
        Assert.Same(normalMatches, editor.VisibleNodes);
        editor.ShowOnlyExcluded = true;
        editor.SearchText = "Assets";
        await editor.SearchAsync();
        Assert.Equal("Assets/cache", Assert.Single(editor.VisibleNodes).RelativePath);
        editor.SearchText = "";
        Assert.Equal(3, editor.VisibleNodes.Count);
        editor.ShowOnlyExcluded = false;
        Assert.Same(editor.RootNodes, editor.VisibleNodes);
    }

    [Fact]
    public async Task FilteredMissingPathsAndEmptySearchFeedbackFollowTheVisibleResult()
    {
        using var fixture = new MainWindowFixture();
        fixture.CreateScannedFile("Assets/file.txt");
        using var editor = new FolderExclusionsViewModel(fixture.Localization, fixture.RootPath,
            new FolderExclusionSet(["missing/cache"]), new FolderBrowser());
        await editor.InitializeAsync();
        editor.ShowOnlyExcluded = true;
        editor.SearchText = "cache";
        await editor.SearchAsync();
        Assert.Equal("missing/cache", Assert.Single(editor.VisibleNodes).RelativePath);
        Assert.False(editor.HasWarning);
        editor.ShowOnlyExcluded = false;
        Assert.Empty(editor.VisibleNodes);
        Assert.Equal(fixture.Localization["FolderSearchEmpty"], editor.Warning);
        editor.SearchText = "Assets";
        await editor.SearchAsync();
        Assert.Single(editor.VisibleNodes);
        Assert.False(editor.HasWarning);
        editor.ShowOnlyExcluded = true;
        Assert.Empty(editor.VisibleNodes);
        Assert.True(editor.HasWarning);
        editor.SearchText = " ";
        Assert.Single(editor.VisibleNodes);
        Assert.False(editor.HasWarning);
    }

    [Theory]
    [InlineData(LanguageMode.English, "1 excluded folder")]
    [InlineData(LanguageMode.Italian, "1 cartella esclusa")]
    [InlineData(LanguageMode.German, "1 ausgeschlossener Ordner")]
    [InlineData(LanguageMode.French, "1 dossier exclu")]
    [InlineData(LanguageMode.Spanish, "1 carpeta excluida")]
    [InlineData(LanguageMode.PortugueseBrazil, "1 pasta excluída")]
    public async Task CountsAndResetUseTheWholeDraftAndCorrectSingular(LanguageMode language, string singular)
    {
        using var fixture = new MainWindowFixture();
        fixture.Localization.SetLanguage(language);
        var initial = new FolderExclusionSet(["missing/cache"]);
        using var editor = new FolderExclusionsViewModel(fixture.Localization, fixture.RootPath, initial, new FolderBrowser());
        await editor.InitializeAsync();
        editor.ShowOnlyExcluded = true;
        Assert.Equal(singular, editor.CountLabel);
        Assert.Single(editor.VisibleNodes).IsExcluded = false;
        Assert.Empty(editor.VisibleNodes);
        Assert.Contains("0", editor.CountLabel, StringComparison.Ordinal);
        editor.SetExcluded("other", true);
        editor.SetExcluded("src/obj", true);
        Assert.Contains("2", editor.CountLabel, StringComparison.Ordinal);
        editor.ResetCommand.Execute(null);
        Assert.True(editor.ShowOnlyExcluded);
        Assert.Equal(initial.Paths, editor.Paths);
        Assert.Equal(singular, editor.CountLabel);
        Assert.True(Assert.Single(editor.VisibleNodes).IsExcluded);
    }

    [Fact]
    public async Task OnlyExcludedPreservesParentCompactionAndUncheckingIncludesTheSubtree()
    {
        using var fixture = new MainWindowFixture();
        using var editor = new FolderExclusionsViewModel(fixture.Localization, fixture.RootPath,
            new FolderExclusionSet(["src/obj", "src", "src-copy"]), new FolderBrowser());
        await editor.InitializeAsync();
        editor.ShowOnlyExcluded = true;
        Assert.Equal(["src", "src-copy"], editor.VisibleNodes.Select(node => node.RelativePath));
        editor.VisibleNodes.Single(node => node.RelativePath == "src").IsExcluded = false;
        Assert.Equal(["src-copy"], editor.Paths);
        Assert.False(editor.Exclusions.Contains("src/obj"));
    }

    [Fact]
    public async Task ApplyingOnlyVisualFilterDoesNotWriteOrRescanAndCancelPreservesStoredRules()
    {
        using var fixture = new MainWindowFixture();
        await fixture.Exclusions.SaveAsync(fixture.RootPath, new FolderExclusionSet(["missing/cache"]), TestContext.Current.CancellationToken);
        fixture.Picker.SourcePath = fixture.RootPath;
        await fixture.ViewModel.ChooseFolderAsync();
        var path = Path.Combine(fixture.RootPath, "local-data", "exclusions.json");
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        fixture.FeatureDialogs.EditHandler = editor =>
        {
            editor.ShowOnlyExcluded = true;
            Assert.True(Assert.Single(editor.VisibleNodes).IsExcluded);
            return Task.FromResult(true);
        };
        await fixture.ViewModel.EditExclusionsAsync();
        Assert.Equal(1, fixture.Scanner.CallCount);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        fixture.FeatureDialogs.EditHandler = editor =>
        {
            editor.ShowOnlyExcluded = true;
            Assert.Single(editor.VisibleNodes).IsExcluded = false;
            return Task.FromResult(false);
        };
        await fixture.ViewModel.EditExclusionsAsync();
        Assert.Equal(1, fixture.Scanner.CallCount);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(["missing/cache"], fixture.Scanner.LastExclusions!.Paths);
    }

    private sealed class CountingBrowser : IFolderBrowser
    {
        private readonly FolderBrowser _inner = new();
        public int Calls { get; private set; }
        public Task<FolderChildren> GetChildrenAsync(string root, string relativeDirectory, CancellationToken cancellationToken)
        {
            Calls++;
            return _inner.GetChildrenAsync(root, relativeDirectory, cancellationToken);
        }
    }
}
