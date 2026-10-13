using ExtSieve.App.Settings;
using ExtSieve.App.Tests.Support;
using ExtSieve.Core.Models;

namespace ExtSieve.App.Tests.ViewModels;

public sealed class ExclusionsPresentationTests
{
    [Theory]
    [InlineData(LanguageMode.English, "excluded folder", "excluded folders")]
    [InlineData(LanguageMode.Italian, "cartella esclusa", "cartelle escluse")]
    [InlineData(LanguageMode.German, "ausgeschlossener Ordner", "ausgeschlossene Ordner")]
    [InlineData(LanguageMode.French, "dossier exclu", "dossiers exclus")]
    [InlineData(LanguageMode.Spanish, "carpeta excluida", "carpetas excluidas")]
    [InlineData(LanguageMode.PortugueseBrazil, "pasta excluída", "pastas excluídas")]
    public async Task SummaryUsesLocalizedZeroSingularAndPluralWithoutChangingSelectionTotals(
        LanguageMode language, string singular, string plural)
    {
        foreach (var count in new[] { 0, 1, 15 })
        {
            using var fixture = new MainWindowFixture();
            var files = new[]
            {
                fixture.CreateScannedFile("same.txt", new byte[1536]),
                fixture.CreateScannedFile("nested/same.txt", [1]),
                fixture.CreateScannedFile("data.json", [])
            };
            fixture.Scanner.Handler = (_, _, _) => Task.FromResult(MainWindowFixture.Result(files));
            fixture.Picker.SourcePath = fixture.RootPath;
            await fixture.Exclusions.SaveAsync(fixture.RootPath,
                new FolderExclusionSet(Enumerable.Range(0, count).Select(index => $"ignored-{index}")), TestContext.Current.CancellationToken);
            await fixture.ViewModel.InitializeAsync();
            await fixture.ViewModel.ChooseFolderAsync();
            fixture.Localization.SetLanguage(language);
            fixture.ViewModel.Flatten = true;
            fixture.ViewModel.Groups.SelectAll();

            var expected = $"{count} {(count == 1 ? singular : plural)}";
            var segments = fixture.ViewModel.SelectionSummary.Split(" · ");
            Assert.Equal(5, segments.Length);
            Assert.Equal(expected, segments[3]);
            Assert.Equal(expected, fixture.ViewModel.ExcludedFoldersDisplay);
            Assert.StartsWith("2 ", segments[0], StringComparison.Ordinal);
            Assert.StartsWith("3 ", segments[1], StringComparison.Ordinal);
            Assert.Contains("KiB", segments[2], StringComparison.Ordinal);
            Assert.StartsWith("1 ", segments[4], StringComparison.Ordinal);
            Assert.Equal(2, fixture.ViewModel.Groups.SelectedGroupCount);
            Assert.Equal(3, fixture.ViewModel.Groups.SelectedFileCount);
            Assert.Equal(1537, fixture.ViewModel.Groups.SelectedBytes);
            Assert.Equal(fixture.Localization["FolderExclusions"], fixture.ViewModel.ExclusionsHelp);
            Assert.DoesNotContain("ignored-", fixture.ViewModel.SelectionSummary, StringComparison.Ordinal);
            Assert.True(fixture.ViewModel.EditExclusionsCommand.CanExecute(null));
        }
    }

    [Fact]
    public async Task ApplyUpdatesSummaryAndCancelPreservesCurrentCountAndSelection()
    {
        using var fixture = new MainWindowFixture();
        fixture.Scanner.Handler = (_, _, _) => Task.FromResult(MainWindowFixture.Result(fixture.CreateScannedFile("file.txt")));
        fixture.Picker.SourcePath = fixture.RootPath;
        await fixture.ViewModel.ChooseFolderAsync();
        fixture.Localization.SetLanguage(LanguageMode.English);
        fixture.ViewModel.Groups.SelectAll();
        var previous = fixture.ViewModel.SelectionSummary;
        var notified = new List<string>();
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(fixture.ViewModel.SelectionSummary)) notified.Add(fixture.ViewModel.SelectionSummary);
        };
        var dialogCalls = 0;
        fixture.FeatureDialogs.EditHandler = editor =>
        {
            dialogCalls++;
            editor.SetExcluded("ignored", true);
            return Task.FromResult(dialogCalls > 1);
        };

        await fixture.ViewModel.EditExclusionsAsync();
        Assert.Equal(previous, fixture.ViewModel.SelectionSummary);
        Assert.Equal(1, fixture.Scanner.CallCount);
        await fixture.ViewModel.EditExclusionsAsync();
        Assert.Equal(2, dialogCalls);
        Assert.Equal(2, fixture.Scanner.CallCount);
        Assert.Equal("1 excluded folder", fixture.ViewModel.SelectionSummary.Split(" · ")[3]);
        Assert.Contains(notified, summary => summary.Split(" · ")[3] == "1 excluded folder");
        Assert.Equal(0, fixture.ViewModel.Groups.SelectedGroupCount);
        Assert.Equal(["ignored"], (await fixture.Exclusions.LoadAsync(fixture.RootPath, TestContext.Current.CancellationToken)).Value.Paths);
    }

    [Fact]
    public async Task ChangingSourceUsesItsOwnAuthoritativeCount()
    {
        using var fixture = new MainWindowFixture();
        var otherSource = Path.Combine(fixture.RootPath, "other");
        Directory.CreateDirectory(otherSource);
        await fixture.Exclusions.SaveAsync(fixture.RootPath, new FolderExclusionSet(["ignored"]), TestContext.Current.CancellationToken);
        await fixture.Exclusions.SaveAsync(otherSource, new FolderExclusionSet(["first", "second"]), TestContext.Current.CancellationToken);
        fixture.Picker.SourcePath = fixture.RootPath;
        await fixture.ViewModel.ChooseFolderAsync();
        fixture.Localization.SetLanguage(LanguageMode.English);
        Assert.Equal("1 excluded folder", fixture.ViewModel.ExcludedFoldersDisplay);
        fixture.Picker.SourcePath = otherSource;
        await fixture.ViewModel.ChooseFolderAsync();
        Assert.Equal("2 excluded folders", fixture.ViewModel.SelectionSummary.Split(" · ")[3]);
        Assert.Equal(["first", "second"], fixture.Scanner.LastExclusions!.Paths);
    }
}
