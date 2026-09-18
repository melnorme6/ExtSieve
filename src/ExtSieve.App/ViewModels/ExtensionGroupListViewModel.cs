using System.ComponentModel;
using ExtSieve.App.Localization;
using ExtSieve.Core.Models;

namespace ExtSieve.App.ViewModels;

public enum ExtensionSortColumn
{
    Extension,
    FileCount,
    TotalBytes,
}

public enum SortDirection
{
    Ascending,
    Descending,
}

public sealed class ExtensionGroupListViewModel : ObservableObject, IDisposable
{
    private readonly LocalizationService _localization;
    private List<ExtensionGroupRowViewModel> _allRows = [];
    private IReadOnlyList<ExtensionGroupRowViewModel> _visibleRows = [];
    private string _filterText = string.Empty;
    private ExtensionSortColumn _sortColumn = ExtensionSortColumn.Extension;
    private SortDirection _sortDirection = SortDirection.Ascending;
    private bool _isApplyingGlobalSelection;

    public ExtensionGroupListViewModel(LocalizationService localization)
    {
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _localization.CultureChanged += OnCultureChanged;
    }

    public IReadOnlyList<ExtensionGroupRowViewModel> AllRows => _allRows;

    public IReadOnlyList<ExtensionGroupRowViewModel> VisibleRows => _visibleRows;

    public string FilterText
    {
        get => _filterText;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_filterText, value, StringComparison.Ordinal))
            {
                return;
            }

            _filterText = value;
            RaisePropertyChanged();
            RefreshVisibleRows();
        }
    }

    public ExtensionSortColumn SortColumn => _sortColumn;

    public SortDirection SortDirection => _sortDirection;

    public long SelectedGroupCount => _allRows.LongCount(row => row.IsSelected);

    public long SelectedFileCount => _allRows.Where(row => row.IsSelected).Sum(row => row.FileCount);

    public long SelectedBytes => _allRows.Where(row => row.IsSelected).Sum(row => row.TotalBytes);

    public bool? SelectionState => SelectedGroupCount switch
    {
        0 => false,
        var selected when selected == _allRows.Count && _allRows.Count > 0 => true,
        _ => null,
    };

    public void Load(ScanResult scanResult)
    {
        ArgumentNullException.ThrowIfNull(scanResult);
        UnsubscribeRows();
        _allRows = scanResult.Groups
            .Select(group => new ExtensionGroupRowViewModel(
                group,
                GetDisplayLabel(group.ExtensionKey),
                group.FileCount.ToString("N0", _localization.CurrentCulture),
                ByteSizeFormatter.Format(group.TotalBytes, _localization.CurrentCulture)))
            .ToList();
        SubscribeRows();
        _sortColumn = ExtensionSortColumn.Extension;
        _sortDirection = SortDirection.Ascending;
        RaisePropertyChanged(nameof(AllRows));
        RaisePropertyChanged(nameof(SortColumn));
        RaisePropertyChanged(nameof(SortDirection));
        RaiseSelectionSummaryChanged();
        RefreshVisibleRows();
    }

    public void SortBy(ExtensionSortColumn column)
    {
        if (_sortColumn == column)
        {
            _sortDirection = _sortDirection == SortDirection.Ascending
                ? SortDirection.Descending
                : SortDirection.Ascending;
        }
        else
        {
            _sortColumn = column;
            _sortDirection = SortDirection.Ascending;
        }

        RaisePropertyChanged(nameof(SortColumn));
        RaisePropertyChanged(nameof(SortDirection));
        RefreshVisibleRows();
    }

    public void SelectAll()
    {
        ApplyGlobalSelection(isSelected: true);
    }

    public void ClearSelection()
    {
        ApplyGlobalSelection(isSelected: false);
    }

    public void ToggleAllSelection()
    {
        if (SelectionState is true)
        {
            ClearSelection();
        }
        else
        {
            SelectAll();
        }
    }

    public void Dispose()
    {
        _localization.CultureChanged -= OnCultureChanged;
        UnsubscribeRows();
    }

    private void RefreshVisibleRows()
    {
        IEnumerable<ExtensionGroupRowViewModel> rows = _allRows;
        if (!string.IsNullOrWhiteSpace(_filterText))
        {
            rows = rows.Where(row =>
                row.DisplayLabel.Contains(_filterText, StringComparison.CurrentCultureIgnoreCase)
                || row.ExtensionKey.Contains(_filterText, StringComparison.OrdinalIgnoreCase));
        }

        IOrderedEnumerable<ExtensionGroupRowViewModel> orderedRows = _sortColumn switch
        {
            ExtensionSortColumn.Extension when _sortDirection == SortDirection.Descending
                => rows.OrderByDescending(row => row.ExtensionKey, StringComparer.Ordinal),
            ExtensionSortColumn.Extension
                => rows.OrderBy(row => row.ExtensionKey, StringComparer.Ordinal),
            ExtensionSortColumn.FileCount when _sortDirection == SortDirection.Descending
                => rows.OrderByDescending(row => row.FileCount)
                    .ThenBy(row => row.ExtensionKey, StringComparer.Ordinal),
            ExtensionSortColumn.FileCount
                => rows.OrderBy(row => row.FileCount)
                    .ThenBy(row => row.ExtensionKey, StringComparer.Ordinal),
            ExtensionSortColumn.TotalBytes when _sortDirection == SortDirection.Descending
                => rows.OrderByDescending(row => row.TotalBytes)
                    .ThenBy(row => row.ExtensionKey, StringComparer.Ordinal),
            _ => rows.OrderBy(row => row.TotalBytes)
                .ThenBy(row => row.ExtensionKey, StringComparer.Ordinal),
        };

        _visibleRows = orderedRows.ToArray();
        RaisePropertyChanged(nameof(VisibleRows));
    }

    private string GetDisplayLabel(string extensionKey)
    {
        return string.IsNullOrEmpty(extensionKey) ? _localization["NoExtension"] : extensionKey;
    }

    private void OnCultureChanged(object? sender, EventArgs eventArgs)
    {
        foreach (var row in _allRows)
        {
            row.DisplayLabel = GetDisplayLabel(row.ExtensionKey);
            row.FileCountDisplay = row.FileCount.ToString("N0", _localization.CurrentCulture);
            row.TotalBytesDisplay = ByteSizeFormatter.Format(
                row.TotalBytes,
                _localization.CurrentCulture);
        }

        RefreshVisibleRows();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (!_isApplyingGlobalSelection
            && eventArgs.PropertyName == nameof(ExtensionGroupRowViewModel.IsSelected))
        {
            RaiseSelectionSummaryChanged();
        }
    }

    private void ApplyGlobalSelection(bool isSelected)
    {
        var changed = false;
        _isApplyingGlobalSelection = true;
        try
        {
            foreach (var row in _allRows)
            {
                if (row.IsSelected == isSelected)
                {
                    continue;
                }

                row.IsSelected = isSelected;
                changed = true;
            }
        }
        finally
        {
            _isApplyingGlobalSelection = false;
        }

        if (changed)
        {
            RaiseSelectionSummaryChanged();
        }
    }

    private void SubscribeRows()
    {
        foreach (var row in _allRows)
        {
            row.PropertyChanged += OnRowPropertyChanged;
        }
    }

    private void UnsubscribeRows()
    {
        foreach (var row in _allRows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }
    }

    private void RaiseSelectionSummaryChanged()
    {
        RaisePropertyChanged(nameof(SelectedGroupCount));
        RaisePropertyChanged(nameof(SelectedFileCount));
        RaisePropertyChanged(nameof(SelectedBytes));
        RaisePropertyChanged(nameof(SelectionState));
    }
}
