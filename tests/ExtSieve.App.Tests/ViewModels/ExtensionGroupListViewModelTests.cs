using ExtSieve.App.Localization;
using ExtSieve.App.Settings;
using ExtSieve.App.ViewModels;
using ExtSieve.Core.Models;

namespace ExtSieve.App.Tests.ViewModels;

public sealed class ExtensionGroupListViewModelTests
{
    [Fact]
    public void LoadUsesInitialExtensionAscendingOrder()
    {
        using var viewModel = CreateViewModel(
            new ExtensionGroup(".txt", 3, 30),
            new ExtensionGroup(string.Empty, 1, 10),
            new ExtensionGroup(".json", 2, 20));

        Assert.Equal([string.Empty, ".json", ".txt"],
            viewModel.VisibleRows.Select(row => row.ExtensionKey));
        Assert.Equal(ExtensionSortColumn.Extension, viewModel.SortColumn);
        Assert.Equal(SortDirection.Ascending, viewModel.SortDirection);
    }

    [Fact]
    public void FilterDoesNotClearHiddenSelections()
    {
        using var viewModel = CreateViewModel(
            new ExtensionGroup(".json", 2, 20),
            new ExtensionGroup(".txt", 3, 30));
        viewModel.AllRows.Single(row => row.ExtensionKey == ".txt").IsSelected = true;

        viewModel.FilterText = "json";

        Assert.Single(viewModel.VisibleRows);
        Assert.Equal(".json", viewModel.VisibleRows[0].ExtensionKey);
        Assert.True(viewModel.AllRows.Single(row => row.ExtensionKey == ".txt").IsSelected);
        Assert.Equal(1, viewModel.SelectedGroupCount);
        Assert.Equal(3, viewModel.SelectedFileCount);
        Assert.Equal(30, viewModel.SelectedBytes);
    }

    [Fact]
    public void SelectAllAndClearAffectFilteredOutRows()
    {
        using var viewModel = CreateViewModel(
            new ExtensionGroup(".json", 2, 20),
            new ExtensionGroup(".txt", 3, 30));
        viewModel.FilterText = "json";

        viewModel.SelectAll();

        Assert.All(viewModel.AllRows, row => Assert.True(row.IsSelected));
        Assert.Equal(2, viewModel.SelectedGroupCount);
        Assert.Equal(5, viewModel.SelectedFileCount);
        Assert.Equal(50, viewModel.SelectedBytes);

        viewModel.ClearSelection();
        Assert.All(viewModel.AllRows, row => Assert.False(row.IsSelected));
        Assert.Equal(0, viewModel.SelectedGroupCount);
        Assert.Equal(0, viewModel.SelectedFileCount);
        Assert.Equal(0, viewModel.SelectedBytes);
    }

    [Fact]
    public void TriStateAndToggleCoverTheEntireDatasetWhileFiltered()
    {
        using var viewModel = CreateViewModel(
            new ExtensionGroup(".json", 2, 20),
            new ExtensionGroup(".txt", 3, 30),
            new ExtensionGroup(".xml", 1, 10));
        Assert.False(viewModel.SelectionState);
        viewModel.AllRows.Single(row => row.ExtensionKey == ".txt").IsSelected = true;
        viewModel.FilterText = "json";

        Assert.Null(viewModel.SelectionState);
        viewModel.ToggleAllSelection();

        Assert.True(viewModel.SelectionState);
        Assert.All(viewModel.AllRows, row => Assert.True(row.IsSelected));
        viewModel.ToggleAllSelection();
        Assert.False(viewModel.SelectionState);
        Assert.All(viewModel.AllRows, row => Assert.False(row.IsSelected));
    }

    [Fact]
    public void GlobalSelectionBatchesAggregateNotificationsAndPreservesRowNotifications()
    {
        using var viewModel = CreateViewModel(
            new ExtensionGroup(".json", 2, 20),
            new ExtensionGroup(".txt", 3, 30),
            new ExtensionGroup(".xml", 1, 10));
        viewModel.AllRows.Single(row => row.ExtensionKey == ".json").IsSelected = true;
        var aggregateNotifications = new List<string?>();
        var rowSelectionNotifications = 0;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName is nameof(ExtensionGroupListViewModel.SelectedGroupCount)
                or nameof(ExtensionGroupListViewModel.SelectedFileCount)
                or nameof(ExtensionGroupListViewModel.SelectedBytes)
                or nameof(ExtensionGroupListViewModel.SelectionState))
            {
                aggregateNotifications.Add(eventArgs.PropertyName);
            }
        };
        foreach (var row in viewModel.AllRows)
        {
            row.PropertyChanged += (_, eventArgs) =>
            {
                if (eventArgs.PropertyName == nameof(ExtensionGroupRowViewModel.IsSelected))
                {
                    rowSelectionNotifications++;
                }
            };
        }

        viewModel.SelectAll();

        Assert.Equal(2, rowSelectionNotifications);
        Assert.Equal(4, aggregateNotifications.Count);
        Assert.Single(aggregateNotifications, nameof(ExtensionGroupListViewModel.SelectedGroupCount));
        Assert.Single(aggregateNotifications, nameof(ExtensionGroupListViewModel.SelectedFileCount));
        Assert.Single(aggregateNotifications, nameof(ExtensionGroupListViewModel.SelectedBytes));
        Assert.Single(aggregateNotifications, nameof(ExtensionGroupListViewModel.SelectionState));
        Assert.Equal(3, viewModel.SelectedGroupCount);
        Assert.Equal(6, viewModel.SelectedFileCount);
        Assert.Equal(60, viewModel.SelectedBytes);

        aggregateNotifications.Clear();
        viewModel.SelectAll();
        Assert.Empty(aggregateNotifications);

        viewModel.ClearSelection();
        Assert.Equal(5, rowSelectionNotifications);
        Assert.Equal(4, aggregateNotifications.Count);
        Assert.False(viewModel.SelectionState);
    }

    [Fact]
    public void NumericSortUsesExtensionAscendingAsTieBreaker()
    {
        using var viewModel = CreateViewModel(
            new ExtensionGroup(".txt", 2, 10),
            new ExtensionGroup(".json", 2, 30),
            new ExtensionGroup(".png", 1, 30));

        viewModel.SortBy(ExtensionSortColumn.FileCount);
        Assert.Equal([".png", ".json", ".txt"],
            viewModel.VisibleRows.Select(row => row.ExtensionKey));

        viewModel.SortBy(ExtensionSortColumn.FileCount);
        Assert.Equal([".json", ".txt", ".png"],
            viewModel.VisibleRows.Select(row => row.ExtensionKey));

        viewModel.SortBy(ExtensionSortColumn.TotalBytes);
        Assert.Equal([".txt", ".json", ".png"],
            viewModel.VisibleRows.Select(row => row.ExtensionKey));
    }

    [Fact]
    public void LanguageChangeUpdatesExtensionlessDisplayLabel()
    {
        var localization = new LocalizationService(LanguageMode.English);
        using var viewModel = new ExtensionGroupListViewModel(localization);
        viewModel.Load(Result(
            new ExtensionGroup(string.Empty, 1, 1),
            new ExtensionGroup(".txt", 2, 20)));
        var extensionlessRow = viewModel.VisibleRows.Single(row => row.ExtensionKey.Length == 0);
        var normalRow = viewModel.VisibleRows.Single(row => row.ExtensionKey == ".txt");
        var englishLabel = extensionlessRow.DisplayLabel;

        localization.SetLanguage(LanguageMode.Italian);

        Assert.NotEqual(englishLabel, extensionlessRow.DisplayLabel);
        Assert.Equal("Nessuna estensione", extensionlessRow.DisplayLabel);
        Assert.Equal(".txt", normalRow.DisplayLabel);
    }

    private static ExtensionGroupListViewModel CreateViewModel(params ExtensionGroup[] groups)
    {
        var viewModel = new ExtensionGroupListViewModel(
            new LocalizationService(LanguageMode.English));
        viewModel.Load(Result(groups));
        return viewModel;
    }

    private static ScanResult Result(params ExtensionGroup[] groups)
    {
        return new ScanResult([], groups, [], TimeSpan.Zero);
    }
}
