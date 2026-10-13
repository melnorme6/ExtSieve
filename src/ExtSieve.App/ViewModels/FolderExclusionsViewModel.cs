using System.Collections.ObjectModel;
using ExtSieve.App.Localization;
using ExtSieve.Core.Models;
using ExtSieve.Core.Services;
using ExtSieve.Core.Utilities;

namespace ExtSieve.App.ViewModels;

public sealed class FolderExclusionsViewModel : LocalizedDialogViewModel
{
    private readonly string _root;
    private readonly IFolderBrowser _browser;
    private readonly FolderExclusionSet _initial;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<FolderNodeViewModel> _nodes = [];
    private CancellationTokenSource? _searchCancellation;
    private FolderExclusionSet _draft;
    private string _searchText = "";
    private string? _warningKey;
    private bool _searching;
    private bool _showOnlyExcluded;
    private string _searchQuery = "";
    private IReadOnlyList<FolderNodeViewModel>? _searchResults;

    public FolderExclusionsViewModel(LocalizationService localization, string root, FolderExclusionSet initial,
        IFolderBrowser browser) : base(localization)
    {
        _root = root;
        _initial = initial;
        _draft = initial;
        _browser = browser;
        SearchCommand = new AsyncCommand(SearchAsync, () => !_searching);
        CancelSearchCommand = new RelayCommand(() => _searchCancellation?.Cancel(), () => _searching);
        ResetCommand = new RelayCommand(() => ReplaceDraft(_initial));
        DeselectAllCommand = new RelayCommand(() => { if (CanDeselectAll) ReplaceDraft(new FolderExclusionSet([])); }, () => CanDeselectAll);
    }

    public string Title => Localization["FolderExclusions"];
    public string Help => Localization["ExclusionsHelp"];
    public string SearchPlaceholder => Localization["SearchFolders"];
    public string SearchLabel => Localization["SearchAction"];
    public string ApplyLabel => Localization["ApplyExclusions"];
    public string ResetLabel => Localization["ResetChanges"];
    public string DeselectAllLabel => Localization["DeselectAllExclusions"];
    public bool CanDeselectAll => !_searching && _draft.Paths.Count > 0;
    public string ShowOnlyExcludedLabel => Localization["ShowOnlyExcluded"];
    public string CountLabel => Format(_draft.Paths.Count == 1 ? "ExcludedFolderSingularFormat" : "ExcludedFolderPluralFormat", _draft.Paths.Count);
    public string Warning => _warningKey is null ? "" : Localization[_warningKey];
    public bool HasWarning => _warningKey is not null;
    public bool IsSearching => _searching;
    public bool CanApply => !_searching;
    public FolderExclusionSet Exclusions => _draft;
    public IReadOnlyList<string> Paths => _draft.Paths;
    public IReadOnlyList<FolderNodeViewModel> RootNodes { get; private set; } = [];
    public IReadOnlyList<FolderNodeViewModel> VisibleNodes { get; private set; } = [];
    public AsyncCommand SearchCommand { get; }
    public RelayCommand CancelSearchCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand DeselectAllCommand { get; }
    public bool ShowOnlyExcluded
    {
        get => _showOnlyExcluded;
        set
        {
            if (_showOnlyExcluded == value) return;
            _showOnlyExcluded = value;
            RaisePropertyChanged();
            RefreshVisibleNodes();
        }
    }
    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value ?? "";
            RaisePropertyChanged();
            if (string.IsNullOrWhiteSpace(_searchText) && !_searching) ClearSearch();
        }
    }

    public async Task InitializeAsync()
    {
        var result = await _browser.GetChildrenAsync(_root, "", _lifetime.Token).ConfigureAwait(true);
        RootNodes = result.RelativePaths.Select(path => CreateNode(path)).ToArray();
        RefreshVisibleNodes();
        if (result.HasWarnings) SetWarning("FolderBrowseWarning");
        RaisePropertyChanged(nameof(RootNodes));
    }

    internal FolderNodeViewModel CreateNode(string path, bool searchResult = false)
    {
        var node = new FolderNodeViewModel(this, path, searchResult);
        _nodes.Add(node);
        return node;
    }

    internal async Task LoadChildrenAsync(FolderNodeViewModel node)
    {
        try
        {
            var result = await _browser.GetChildrenAsync(_root, node.RelativePath, _lifetime.Token).ConfigureAwait(true);
            if (_lifetime.IsCancellationRequested) return;
            node.Children.Clear();
            foreach (var path in result.RelativePaths) node.Children.Add(CreateNode(path));
            if (result.HasWarnings) SetWarning("FolderBrowseWarning");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    internal bool IsExcluded(string path) => _draft.Contains(path);
    internal bool IsInherited(string path) => _draft.Paths.Any(parent => path.StartsWith(parent + "/", PathRules.PlatformComparison));

    public void SetExcluded(string path, bool excluded)
    {
        var normalized = FolderExclusionSet.NormalizePath(path);
        ReplaceDraft(new FolderExclusionSet(excluded ? _draft.Paths.Append(normalized)
            : _draft.Paths.Where(item => !string.Equals(item, normalized, PathRules.PlatformComparison))));
    }

    private void ReplaceDraft(FolderExclusionSet value)
    {
        _draft = value;
        foreach (var node in _nodes) node.RefreshSelection();
        RaisePropertyChanged(nameof(Paths)); RaisePropertyChanged(nameof(CountLabel));
        RaisePropertyChanged(nameof(CanDeselectAll)); DeselectAllCommand.RaiseCanExecuteChanged();
        RefreshVisibleNodes();
    }

    private static bool MatchesQuery(string path, string query) => path.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void RefreshVisibleNodes()
    {
        // Relative paths retain context and keep unloaded or missing exclusions editable without filesystem traversal.
        VisibleNodes = _showOnlyExcluded
            ? _draft.Paths.Where(path => MatchesQuery(path, _searchQuery)).Select(path => new FolderNodeViewModel(this, path, true)).ToArray()
            : _searchResults ?? RootNodes;
        RaisePropertyChanged(nameof(VisibleNodes));
        if (_warningKey == "FolderSearchEmpty") _warningKey = null;
        if (_searchQuery.Length > 0 && VisibleNodes.Count == 0 && _warningKey is null) _warningKey = "FolderSearchEmpty";
        RaisePropertyChanged(nameof(Warning)); RaisePropertyChanged(nameof(HasWarning));
    }

    private void ClearSearch()
    {
        _searchQuery = "";
        _searchResults = null;
        RefreshVisibleNodes();
    }

    public async Task SearchAsync()
    {
        if (_searching) return;
        if (string.IsNullOrWhiteSpace(_searchText)) { ClearSearch(); return; }
        var query = _searchText.Trim();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _searchCancellation = cancellation;
        SetSearching(true);
        var matches = new List<FolderNodeViewModel>();
        var pending = new Stack<string>();
        pending.Push("");
        var visited = 0;
        _warningKey = null;
        try
        {
            while (pending.TryPop(out var parent))
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var result = await _browser.GetChildrenAsync(_root, parent, cancellation.Token).ConfigureAwait(true);
                if (result.HasWarnings) SetWarning("FolderBrowseWarning");
                foreach (var path in result.RelativePaths)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (matches.Count < 500 && MatchesQuery(path, query)) matches.Add(CreateNode(path, true));
                    // An excluded parent represents its whole subtree; searching need not enter it.
                    if (!_draft.Contains(path)) pending.Push(path);
                }
                if (++visited >= 10000 || matches.Count >= 500) { SetWarning("FolderSearchLimit"); break; }
            }
            if (string.Equals(query, _searchText.Trim(), StringComparison.Ordinal))
            {
                _searchResults = matches;
                _searchQuery = query;
                RefreshVisibleNodes();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            _searchCancellation = null;
            SetSearching(false);
            if (string.IsNullOrWhiteSpace(_searchText)) ClearSearch();
            RaisePropertyChanged(nameof(Warning)); RaisePropertyChanged(nameof(HasWarning));
        }
    }

    private void SetSearching(bool value)
    {
        _searching = value;
        RaisePropertyChanged(nameof(IsSearching)); RaisePropertyChanged(nameof(CanApply));
        SearchCommand.RaiseCanExecuteChanged(); CancelSearchCommand.RaiseCanExecuteChanged();
        RaisePropertyChanged(nameof(CanDeselectAll)); DeselectAllCommand.RaiseCanExecuteChanged();
    }
    private void SetWarning(string key) { _warningKey = key; RaisePropertyChanged(nameof(Warning)); RaisePropertyChanged(nameof(HasWarning)); }
    public override void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); base.Dispose(); }
}

public sealed class FolderNodeViewModel : ObservableObject
{
    private readonly FolderExclusionsViewModel? _owner;
    private bool _expanded;
    private bool _loaded;
    private readonly bool _searchResult;

    private FolderNodeViewModel() { RelativePath = ""; IsPlaceholder = true; }
    internal FolderNodeViewModel(FolderExclusionsViewModel owner, string relativePath, bool searchResult)
    {
        _owner = owner;
        RelativePath = relativePath;
        _searchResult = searchResult;
        if (!searchResult) Children.Add(new FolderNodeViewModel());
    }
    public string RelativePath { get; }
    public string DisplayName => _searchResult ? RelativePath : RelativePath.Split('/').Last();
    public bool IsPlaceholder { get; }
    public bool CanToggle => !IsPlaceholder && !_owner!.IsInherited(RelativePath);
    public bool IsExcluded
    {
        get => !IsPlaceholder && _owner!.IsExcluded(RelativePath);
        set { if (CanToggle) _owner!.SetExcluded(RelativePath, value); }
    }
    public ObservableCollection<FolderNodeViewModel> Children { get; } = [];
    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            _expanded = value;
            RaisePropertyChanged();
            if (value && !_loaded && !IsPlaceholder && !_searchResult) { _loaded = true; _ = _owner!.LoadChildrenAsync(this); }
        }
    }
    internal void RefreshSelection() { RaisePropertyChanged(nameof(IsExcluded)); RaisePropertyChanged(nameof(CanToggle)); }
}
