using ExtSieve.App.Settings;
using ExtSieve.App.Tests.Support;
using ExtSieve.App.ViewModels;
using ExtSieve.Core.Models;
using ExtSieve.Core.Services;

namespace ExtSieve.App.Tests.ViewModels;

public sealed class InterfacePolishViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeselectAllClearsWholeDraftAndResetRestoresOpeningRules(bool filtered)
    {
        using var fixture = new MainWindowFixture();
        fixture.CreateScannedFile("src/obj/file.txt");
        var initial = new FolderExclusionSet(["src", "missing/cache"]);
        using var editor = new FolderExclusionsViewModel(fixture.Localization, fixture.RootPath, initial, new FolderBrowser());
        await editor.InitializeAsync();
        var src = editor.RootNodes.Single(node => node.RelativePath == "src");
        editor.ShowOnlyExcluded = filtered;
        editor.SetExcluded("added", true);
        var notifications = 0;
        editor.DeselectAllCommand.CanExecuteChanged += (_, _) => notifications++;
        Assert.True(editor.DeselectAllCommand.CanExecute(null));
        editor.DeselectAllCommand.Execute(null);
        Assert.Empty(editor.Paths);
        Assert.False(src.IsExcluded);
        Assert.Equal(string.Format(fixture.Localization.CurrentCulture, fixture.Localization["ExcludedFolderPluralFormat"], 0), editor.CountLabel);
        Assert.False(editor.DeselectAllCommand.CanExecute(null));
        Assert.Equal(1, notifications);
        if (filtered) Assert.Empty(editor.VisibleNodes);
        else Assert.Same(editor.RootNodes, editor.VisibleNodes);
        editor.ResetCommand.Execute(null);
        Assert.Same(initial, editor.Exclusions);
        Assert.True(src.IsExcluded);
        Assert.True(editor.DeselectAllCommand.CanExecute(null));
        Assert.Equal(filtered, editor.ShowOnlyExcluded);
        Assert.Equal(2, notifications);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DeselectAllPersistsOnlyAfterApplyAndResetKeepsOriginalConfiguration(bool apply, bool reset)
    {
        using var fixture = new MainWindowFixture();
        var initial = new FolderExclusionSet(["missing/cache", "src/obj"]);
        await fixture.Exclusions.SaveAsync(fixture.RootPath, initial, TestContext.Current.CancellationToken);
        fixture.Picker.SourcePath = fixture.RootPath;
        await fixture.ViewModel.ChooseFolderAsync();
        var path = Path.Combine(fixture.RootPath, "local-data", "exclusions.json");
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        fixture.FeatureDialogs.EditHandler = async editor =>
        {
            editor.ShowOnlyExcluded = true;
            editor.DeselectAllCommand.Execute(null);
            Assert.Empty(editor.Paths);
            Assert.Empty(editor.VisibleNodes);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal(1, fixture.Scanner.CallCount);
            if (reset) editor.ResetCommand.Execute(null);
            return apply;
        };
        await fixture.ViewModel.EditExclusionsAsync();
        var changed = apply && !reset;
        Assert.Equal(changed ? 2 : 1, fixture.Scanner.CallCount);
        Assert.Equal(changed ? [] : initial.Paths, (await fixture.Exclusions.LoadAsync(fixture.RootPath, TestContext.Current.CancellationToken)).Value.Paths);
        Assert.Equal(changed ? [] : initial.Paths, fixture.Scanner.LastExclusions!.Paths);
        if (!changed) Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeselectAllIsDisabledDuringSearchAndForEmptyDraft()
    {
        using var fixture = new MainWindowFixture();
        using var editor = new FolderExclusionsViewModel(fixture.Localization, fixture.RootPath,
            new FolderExclusionSet(["missing"]), new WaitingBrowser());
        editor.SearchText = "cache";
        var search = editor.SearchAsync();
        Assert.False(editor.DeselectAllCommand.CanExecute(null));
        editor.DeselectAllCommand.Execute(null);
        Assert.Equal(["missing"], editor.Paths);
        editor.CancelSearchCommand.Execute(null);
        await search;
        Assert.True(editor.DeselectAllCommand.CanExecute(null));
        editor.DeselectAllCommand.Execute(null);
        Assert.False(editor.DeselectAllCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(LanguageMode.English, "1 extension", "2 extensions", "0 extensions")]
    [InlineData(LanguageMode.Italian, "1 estensione", "2 estensioni", "0 estensioni")]
    [InlineData(LanguageMode.German, "1 Erweiterung", "2 Erweiterungen", "0 Erweiterungen")]
    [InlineData(LanguageMode.French, "1 extension", "2 extensions", "0 extensions")]
    [InlineData(LanguageMode.Spanish, "1 extensi\u00f3n", "2 extensiones", "0 extensiones")]
    [InlineData(LanguageMode.PortugueseBrazil, "1 extens\u00e3o", "2 extens\u00f5es", "0 extens\u00f5es")]
    public async Task ExtensionFooterCountUpdatesAcrossEditingSelectionImportAndCopy(LanguageMode language, string singular, string plural, string zero)
    {
        using var fixture = new MainWindowFixture(language);
        await fixture.ViewModel.InitializeAsync();
        fixture.Localization.SetLanguage(language);
        var manager = fixture.ViewModel.Profiles;
        Assert.Equal(zero, manager.ExtensionSummary);
        fixture.FeatureDialogs.Name = "Unity";
        await manager.CreateAsync();
        Assert.Equal(zero, manager.ExtensionSummary);
        fixture.FeatureDialogs.ExtensionHandler = _ => Task.FromResult<string?>(".c");
        await manager.AddExtensionAsync();
        Assert.Equal(singular, manager.ExtensionSummary);
        fixture.FeatureDialogs.ExtensionHandler = _ => Task.FromResult<string?>(".json");
        await manager.AddExtensionAsync();
        Assert.Equal(plural, manager.ExtensionSummary);
        await manager.RemoveExtensionAsync(".c");
        Assert.Equal(singular, manager.ExtensionSummary);
        fixture.FeatureDialogs.Name = "Renamed";
        await manager.RenameAsync();
        Assert.Equal(singular, manager.ExtensionSummary);
        fixture.FeatureDialogs.Name = "Copy";
        await manager.DuplicateAsync();
        Assert.Equal(singular, manager.ExtensionSummary);
        fixture.FeatureDialogs.ImportHandler = () => Task.FromResult<IReadOnlyList<ExtensionProfile>?>([ExtensionProfile.Create("Imported", [".cs", ".txt"])]);
        await manager.ImportAsync();
        Assert.Equal(plural, manager.ExtensionSummary);
        manager.SelectedProfile = manager.Profiles.Single(profile => profile.Name == "Renamed");
        Assert.Equal(singular, manager.ExtensionSummary);
        await manager.RemoveExtensionAsync(".json");
        Assert.Equal(zero, manager.ExtensionSummary);
        Assert.True(manager.ExtensionItems[^1].IsAdd);
    }

    private sealed class WaitingBrowser : IFolderBrowser
    {
        public async Task<FolderChildren> GetChildrenAsync(string root, string relativeDirectory, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new FolderChildren([], false);
        }
    }
}
