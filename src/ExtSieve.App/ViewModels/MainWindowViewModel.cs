using System.ComponentModel;
using ExtSieve.App.Localization;
using ExtSieve.App.Services;
using ExtSieve.App.Settings;
using ExtSieve.App.Themes;
using ExtSieve.Core.Models;
using ExtSieve.Core.Services;

namespace ExtSieve.App.ViewModels;

public enum MainWindowState
{
    NoSource,
    Scanning,
    EmptyResult,
    Results,
    ResultsWithWarnings,
    CreatingArchive,
    Cancelled,
    Success,
    Error,
}

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly LocalizationService _localization;
    private readonly IFolderScanner _folderScanner;
    private readonly IArchivePlanner _archivePlanner;
    private readonly IZipArchiveService _zipArchiveService;
    private readonly IPathPickerService _pathPicker;
    private readonly IUserPromptService _prompt;
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly IProductVersionProvider _productVersionProvider;
    private readonly IProductAboutProvider _productAboutProvider;
    private readonly IExternalResourceService _externalResourceService;
    private readonly SemaphoreSlim _settingsSaveLock = new(1, 1);
    private CancellationTokenSource? _operationCancellation;
    private ScanResult? _scanResult;
    private ArchiveResult? _archiveResult;
    private AppSettings _settings = new();
    private MainWindowState _state = MainWindowState.NoSource;
    private string _sourcePath = string.Empty;
    private string _outputPath = string.Empty;
    private ArchiveMode _archiveMode = ArchiveMode.PreserveStructure;
    private long _scanProgressFiles;
    private int _archiveCompletedFiles;
    private int _archiveTotalFiles;
    private long _archiveCompletedBytes;
    private long _archiveTotalBytes;
    private int _plannedCollisionCount;
    private string _technicalDetails = string.Empty;
    private string _settingsWarning = string.Empty;
    private bool _isInitialized;
    private bool _isInitializingSettings;
    private bool _isDialogOpen;
    private bool _isSettingsViewOpen;
    private LanguageOption _selectedLanguageOption = null!;
    private ThemeOption _selectedThemeOption = null!;

    public MainWindowViewModel(
        LocalizationService localization,
        IFolderScanner folderScanner,
        IArchivePlanner archivePlanner,
        IZipArchiveService zipArchiveService,
        IPathPickerService pathPicker,
        IUserPromptService prompt,
        ISettingsService settingsService,
        IThemeService themeService,
        IProductVersionProvider productVersionProvider,
        IProductAboutProvider productAboutProvider,
        IExternalResourceService externalResourceService)
    {
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _folderScanner = folderScanner ?? throw new ArgumentNullException(nameof(folderScanner));
        _archivePlanner = archivePlanner ?? throw new ArgumentNullException(nameof(archivePlanner));
        _zipArchiveService = zipArchiveService ?? throw new ArgumentNullException(nameof(zipArchiveService));
        _pathPicker = pathPicker ?? throw new ArgumentNullException(nameof(pathPicker));
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _productVersionProvider = productVersionProvider ?? throw new ArgumentNullException(nameof(productVersionProvider));
        _productAboutProvider = productAboutProvider ?? throw new ArgumentNullException(nameof(productAboutProvider));
        _externalResourceService = externalResourceService ?? throw new ArgumentNullException(nameof(externalResourceService));

        Groups = new ExtensionGroupListViewModel(_localization);
        Groups.PropertyChanged += OnGroupsPropertyChanged;
        _localization.CultureChanged += OnCultureChanged;

        ChooseFolderCommand = new AsyncCommand(ChooseFolderAsync, CanChangeSource);
        ScanAgainCommand = new AsyncCommand(ScanAgainAsync, () => CanChangeSource() && HasSource);
        BrowseOutputCommand = new AsyncCommand(BrowseOutputAsync, () => CanEditArchiveOptions);
        CreateArchiveCommand = new AsyncCommand(CreateArchiveAsync, () => CanCreateArchive);
        CancelCommand = new RelayCommand(CancelActiveOperation, () => IsBusy);
        OpenDestinationFolderCommand = new AsyncCommand(
            OpenDestinationFolderAsync,
            () => _archiveResult is not null && !IsBusy);
        ToggleAllSelectionCommand = new RelayCommand(
            Groups.ToggleAllSelection,
            () => HasResults && !IsBusy);
        ClearFilterCommand = new RelayCommand(ClearFilter, () => IsFilterClearVisible);
        OpenSettingsCommand = new RelayCommand(OpenSettings, () => !IsBusy && !IsSettingsViewOpen);
        CloseSettingsCommand = new RelayCommand(CloseSettings, () => IsSettingsViewOpen);
        OpenLicenseCommand = new AsyncCommand(
            OpenLicenseAsync,
            () => _productAboutProvider.LicenseDocumentPath is not null);
        OpenReleasePageCommand = new AsyncCommand(
            OpenReleasePageAsync,
            () => _productAboutProvider.ReleasePageUri is not null);
        OpenSupportPageCommand = new AsyncCommand(
            OpenSupportPageAsync,
            () => _productAboutProvider.SupportPageUri is not null);
        OpenThirdPartyNoticesCommand = new AsyncCommand(
            OpenThirdPartyNoticesAsync,
            () => _productAboutProvider.ThirdPartyNoticesPath is not null);
        SortByExtensionCommand = new RelayCommand(
            () => Groups.SortBy(ExtensionSortColumn.Extension),
            () => HasResults && !IsBusy);
        SortByFileCountCommand = new RelayCommand(
            () => Groups.SortBy(ExtensionSortColumn.FileCount),
            () => HasResults && !IsBusy);
        SortBySizeCommand = new RelayCommand(
            () => Groups.SortBy(ExtensionSortColumn.TotalBytes),
            () => HasResults && !IsBusy);

        RebuildSettingOptions();
    }

    public ExtensionGroupListViewModel Groups { get; }
    public AsyncCommand ChooseFolderCommand { get; }
    public AsyncCommand ScanAgainCommand { get; }
    public AsyncCommand BrowseOutputCommand { get; }
    public AsyncCommand CreateArchiveCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncCommand OpenDestinationFolderCommand { get; }
    public RelayCommand ToggleAllSelectionCommand { get; }
    public RelayCommand ClearFilterCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand CloseSettingsCommand { get; }
    public AsyncCommand OpenLicenseCommand { get; }
    public AsyncCommand OpenReleasePageCommand { get; }
    public AsyncCommand OpenSupportPageCommand { get; }
    public AsyncCommand OpenThirdPartyNoticesCommand { get; }
    public RelayCommand SortByExtensionCommand { get; }
    public RelayCommand SortByFileCountCommand { get; }
    public RelayCommand SortBySizeCommand { get; }

    public string AppTitle => WindowTitleFormatter.Format(
        _localization["AppTitle"],
        _productVersionProvider.PublicVersion);
    public string AppPurpose => _localization["AppPurpose"];
    public string ChooseFolderLabel => HasSource ? _localization["ChooseAnotherFolder"] : _localization["ChooseFolder"];
    public string ScanAgainLabel => _localization["ScanAgain"];
    public string SourceLabel => _localization["SourceFolder"];
    public string SourcePlaceholder => _localization["NoSourceSelected"];
    public string FilesLabel => _localization["Files"];
    public string ExtensionsLabel => _localization["Extensions"];
    public string TotalSizeLabel => _localization["TotalSize"];
    public string SelectionToggleAccessibleName => Groups.SelectionState is true
        ? _localization["ClearAllGroups"]
        : _localization["SelectAllGroups"];
    public string SelectionToggleToolTip => Groups.SelectionState is null
        ? _localization["SelectAllGroupsPartialToolTip"]
        : Groups.SelectionState is true
            ? _localization["ClearAllGroupsToolTip"]
            : _localization["SelectAllGroupsToolTip"];
    public string FilterPlaceholder => _localization["FilterExtensions"];
    public string ClearFilterLabel => _localization["ClearFilter"];
    public string ExtensionHeader => _localization["Extension"];
    public string FileCountHeader => _localization["Files"];
    public string SizeHeader => _localization["Size"];
    public bool IsExtensionSortAscending => IsSort(ExtensionSortColumn.Extension, SortDirection.Ascending);
    public bool IsExtensionSortDescending => IsSort(ExtensionSortColumn.Extension, SortDirection.Descending);
    public bool IsFileCountSortAscending => IsSort(ExtensionSortColumn.FileCount, SortDirection.Ascending);
    public bool IsFileCountSortDescending => IsSort(ExtensionSortColumn.FileCount, SortDirection.Descending);
    public bool IsSizeSortAscending => IsSort(ExtensionSortColumn.TotalBytes, SortDirection.Ascending);
    public bool IsSizeSortDescending => IsSort(ExtensionSortColumn.TotalBytes, SortDirection.Descending);
    public string ExtensionHeaderAccessibleName => BuildSortAccessibleName("Extension", ExtensionSortColumn.Extension);
    public string FileCountHeaderAccessibleName => BuildSortAccessibleName("Files", ExtensionSortColumn.FileCount);
    public string SizeHeaderAccessibleName => BuildSortAccessibleName("Size", ExtensionSortColumn.TotalBytes);
    public string ArchiveLayoutLabel => _localization["ArchiveLayout"];
    public string PreserveStructureLabel => _localization["PreserveStructure"];
    public string FlattenLabel => _localization["Flatten"];
    public string OutputLabel => _localization["Output"];
    public string BrowseLabel => _localization["Browse"];
    public string SaveLabel => _localization["Save"];
    public string CancelLabel => _localization["Cancel"];
    public string SettingsLabel => _localization["Settings"];
    public string LanguageLabel => _localization["Language"];
    public string ThemeLabel => _localization["Theme"];
    public string ThemeDescription => _localization["ThemeDescription"];
    public string LanguageDescription => _localization["LanguageDescription"];
    public string BackToMainViewLabel => _localization["BackToMainView"];
    public string AboutLabel => _localization["About"];
    public string AboutNameLabel => _localization["AboutName"];
    public string AboutVersionLabel => _localization["AboutVersion"];
    public string AboutBuildInfoLabel => _localization["AboutBuildInfo"];
    public string AboutUpdatesLabel => _localization["AboutUpdates"];
    public string AboutLicenseLabel => _localization["AboutLicense"];
    public string AboutReleasePageLabel => _localization["AboutReleasePage"];
    public string AboutSupportProjectLabel => _localization["AboutSupportProject"];
    public string AboutThirdPartyNoticesLabel => _localization["AboutThirdPartyNotices"];
    public string ProductName => _productAboutProvider.ProductName;
    public string ProductVersion => _productVersionProvider.PublicVersion;
    public string ProductBuildInfo => _productAboutProvider.BuildInfo ?? UnavailableValue;
    public string UpdateStatus => UnavailableValue;
    public string ProductLicense => _productAboutProvider.LicenseName ?? UnavailableValue;
    public string ReleasePage => _productAboutProvider.ReleasePageUri?.AbsoluteUri ?? UnavailableValue;
    public string SupportProject => HasSupportPage
        ? _localization["DonateOnKofi"]
        : UnavailableValue;
    public string ThirdPartyNotices => HasThirdPartyNotices
        ? _localization["OpenThirdPartyNotices"]
        : UnavailableValue;
    public bool HasLicenseDocument => _productAboutProvider.LicenseDocumentPath is not null;
    public bool ShowsStaticLicense => !HasLicenseDocument;
    public bool HasReleasePage => _productAboutProvider.ReleasePageUri is not null;
    public bool ShowsStaticReleasePage => !HasReleasePage;
    public bool HasSupportPage => _productAboutProvider.SupportPageUri is not null;
    public bool ShowsStaticSupportPage => !HasSupportPage;
    public bool HasThirdPartyNotices => _productAboutProvider.ThirdPartyNoticesPath is not null;
    public bool ShowsStaticThirdPartyNotices => !HasThirdPartyNotices;
    public string ProductNameAccessibleName => FormatAboutValue(AboutNameLabel, ProductName);
    public string ProductVersionAccessibleName => FormatAboutValue(AboutVersionLabel, ProductVersion);
    public string ProductBuildInfoAccessibleName => FormatAboutValue(AboutBuildInfoLabel, ProductBuildInfo);
    public string UpdateStatusAccessibleName => FormatAboutValue(AboutUpdatesLabel, UpdateStatus);
    public string ProductLicenseAccessibleName => HasLicenseDocument
        ? _localization["OpenLicense"]
        : FormatAboutValue(AboutLicenseLabel, ProductLicense);
    public string ReleasePageAccessibleName => HasReleasePage
        ? _localization["OpenReleasePage"]
        : FormatAboutValue(AboutReleasePageLabel, ReleasePage);
    public string SupportProjectAccessibleName => HasSupportPage
        ? _localization["DonateOnKofi"]
        : FormatAboutValue(AboutSupportProjectLabel, SupportProject);
    public string ThirdPartyNoticesAccessibleName => HasThirdPartyNotices
        ? _localization["OpenThirdPartyNotices"]
        : FormatAboutValue(AboutThirdPartyNoticesLabel, ThirdPartyNotices);
    public string TechnicalDetailsLabel => _localization["TechnicalDetails"];
    public string OpenDestinationFolderLabel => _localization["OpenDestinationFolder"];
    public string EmptyStateTitle => _localization["EmptyStateTitle"];
    public string EmptyStateDescription => _localization["EmptyStateDescription"];
    public string SettingsWarning => _settingsWarning;

    public string WarningSummary => _scanResult is { Warnings.Count: > 0 } result
        ? Format("WarningSummaryFormat", result.Warnings.Count)
        : string.Empty;

    public string StatusText => IsSettingsViewOpen ? _localization["StatusSettings"] : _state switch
    {
        MainWindowState.NoSource => _localization["StatusNoSource"],
        MainWindowState.Scanning => Format("StatusScanningFormat", _scanProgressFiles),
        MainWindowState.EmptyResult => _localization["StatusEmptyResult"],
        MainWindowState.Results => _localization["StatusResults"],
        MainWindowState.ResultsWithWarnings => _localization["StatusResultsWithWarnings"],
        MainWindowState.CreatingArchive => Format("StatusCreatingFormat", _archiveCompletedFiles, _archiveTotalFiles),
        MainWindowState.Cancelled => _localization["StatusCancelled"],
        MainWindowState.Success => _localization["StatusSuccess"],
        MainWindowState.Error => _localization["StatusError"],
        _ => string.Empty,
    };

    public string SelectionSummary => Format(
        "SelectionSummaryFormat",
        Groups.SelectedGroupCount,
        Groups.SelectedFileCount,
        ByteSizeFormatter.Format(Groups.SelectedBytes, _localization.CurrentCulture),
        _plannedCollisionCount);

    public string SuccessSummary => _archiveResult is null
        ? string.Empty
        : Format(
            "SuccessSummaryFormat",
            _archiveResult.EntryCount,
            ByteSizeFormatter.Format(_archiveResult.ArchiveBytes, _localization.CurrentCulture),
            _archiveResult.Duration.TotalSeconds.ToString("N1", _localization.CurrentCulture),
            _archiveResult.DestinationPath);

    public string SourcePath
    {
        get => _sourcePath;
        private set
        {
            if (string.Equals(_sourcePath, value, PathComparison)) return;
            _sourcePath = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(HasSource));
            RaisePropertyChanged(nameof(ChooseFolderLabel));
        }
    }

    public string OutputPath
    {
        get => _outputPath;
        private set
        {
            if (string.Equals(_outputPath, value, PathComparison)) return;
            _outputPath = value;
            RaisePropertyChanged();
            UpdatePlanPreview();
        }
    }

    public string FilterText
    {
        get => Groups.FilterText;
        set => Groups.FilterText = value;
    }

    public bool IsFilterClearVisible => !string.IsNullOrEmpty(FilterText);

    public bool PreserveStructure
    {
        get => _archiveMode == ArchiveMode.PreserveStructure;
        set { if (value) SetArchiveMode(ArchiveMode.PreserveStructure); }
    }

    public bool Flatten
    {
        get => _archiveMode == ArchiveMode.Flatten;
        set { if (value) SetArchiveMode(ArchiveMode.Flatten); }
    }

    public IReadOnlyList<LanguageOption> LanguageOptions { get; private set; } = [];
    public IReadOnlyList<ThemeOption> ThemeOptions { get; private set; } = [];

    public LanguageOption SelectedLanguageOption
    {
        get => _selectedLanguageOption;
        set
        {
            if (_isInitializingSettings || value is null || _settings.Language == value.Value) return;
            _settings = _settings with { Language = value.Value };
            _selectedLanguageOption = value;
            RaisePropertyChanged();
            _localization.SetLanguage(value.Value);
            QueueSettingsSave();
        }
    }

    public ThemeOption SelectedThemeOption
    {
        get => _selectedThemeOption;
        set
        {
            if (_isInitializingSettings || value is null || _settings.Theme == value.Value) return;
            _settings = _settings with { Theme = value.Value };
            _selectedThemeOption = value;
            RaisePropertyChanged();
            _themeService.Apply(value.Value);
            QueueSettingsSave();
        }
    }

    public MainWindowState State => _state;
    public bool HasSource => !string.IsNullOrWhiteSpace(_sourcePath);
    public bool HasScanSummary => _scanResult is not null;
    public bool HasResults => _scanResult is { Files.Count: > 0 };
    public bool ShowsEmptyState => !HasResults && !IsBusy;
    public bool HasWarnings => _scanResult is { Warnings.Count: > 0 };
    public bool IsBusy => _state is MainWindowState.Scanning or MainWindowState.CreatingArchive;
    public bool IsScanning => _state == MainWindowState.Scanning;
    public bool IsCreatingArchive => _state == MainWindowState.CreatingArchive;
    public bool IsProgressVisible => IsBusy;
    public bool IsSuccess => _state == MainWindowState.Success;
    public bool IsError => _state == MainWindowState.Error;
    public bool HasSettingsWarning => !string.IsNullOrWhiteSpace(_settingsWarning);
    public bool IsSettingsViewOpen => _isSettingsViewOpen;
    public bool IsMainViewOpen => !_isSettingsViewOpen;
    public bool? AllGroupsSelectionState => Groups.SelectionState;
    public bool CanEditArchiveOptions => HasResults && !IsBusy && !_isDialogOpen;
    public bool CanCreateArchive => CanEditArchiveOptions && Groups.SelectedGroupCount > 0 && IsValidDestination(_outputPath);
    public double ArchiveProgressMaximum => Math.Max(1, _archiveTotalBytes);
    public double ArchiveProgressValue => _archiveCompletedBytes;
    public string TotalFilesDisplay => (_scanResult?.Files.Count ?? 0).ToString("N0", _localization.CurrentCulture);
    public string TotalGroupsDisplay => (_scanResult?.Groups.Count ?? 0).ToString("N0", _localization.CurrentCulture);
    public string TotalBytesDisplay => ByteSizeFormatter.Format(_scanResult?.Files.Sum(file => file.Size) ?? 0, _localization.CurrentCulture);
    public string TechnicalDetails => _technicalDetails;

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        _isInitialized = true;
        _isInitializingSettings = true;
        try
        {
            _settings = await _settingsService.LoadAsync().ConfigureAwait(true);
            _localization.SetLanguage(_settings.Language);
            _themeService.Apply(_settings.Theme);
            RebuildSettingOptions();
        }
        finally
        {
            _isInitializingSettings = false;
        }
    }

    public async Task ChooseFolderAsync()
    {
        if (!CanChangeSource()) return;
        _isDialogOpen = true;
        UpdateCommandStates();
        string? selectedPath;
        try
        {
            selectedPath = await _pathPicker.PickSourceFolderAsync(_localization["ChooseFolderDialogTitle"]);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            SetError(exception);
            return;
        }
        finally
        {
            _isDialogOpen = false;
            UpdateCommandStates();
        }

        if (string.IsNullOrWhiteSpace(selectedPath)) return;
        try
        {
            SourcePath = Path.GetFullPath(selectedPath);
            OutputPath = SuggestDestination(SourcePath);
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or UnauthorizedAccessException)
        {
            SetError(exception);
            return;
        }

        await ScanSourceAsync().ConfigureAwait(true);
    }

    public Task ScanAgainAsync() => CanChangeSource() && HasSource ? ScanSourceAsync() : Task.CompletedTask;

    public async Task BrowseOutputAsync()
    {
        if (!CanEditArchiveOptions) return;
        _isDialogOpen = true;
        UpdateCommandStates();
        try
        {
            var selectedPath = await _pathPicker.PickArchiveDestinationAsync(
                _localization["ChooseOutputDialogTitle"], Path.GetFileName(_outputPath));
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                OutputPath = EnsureZipExtension(Path.GetFullPath(selectedPath));
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException)
        {
            SetError(exception);
        }
        finally
        {
            _isDialogOpen = false;
            UpdateCommandStates();
        }
    }

    public async Task CreateArchiveAsync()
    {
        if (!CanCreateArchive || _scanResult is null) return;
        var destination = Path.GetFullPath(_outputPath);
        var replaceExistingDestination = File.Exists(destination);
        if (replaceExistingDestination)
        {
            _isDialogOpen = true;
            UpdateCommandStates();
            bool replace;
            try
            {
                replace = await _prompt.ConfirmAsync(
                    _localization["ReplaceArchiveTitle"],
                    Format("ReplaceArchiveMessageFormat", destination),
                    _localization["Replace"],
                    _localization["Cancel"]);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                SetError(exception);
                return;
            }
            finally
            {
                _isDialogOpen = false;
                UpdateCommandStates();
            }

            if (!replace) return;
        }

        ArchivePlan plan;
        try
        {
            plan = CreateCurrentPlan();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            SetError(exception);
            return;
        }

        _archiveCompletedFiles = 0;
        _archiveTotalFiles = plan.Entries.Count;
        _archiveCompletedBytes = 0;
        _archiveTotalBytes = plan.TotalSourceBytes;
        _archiveResult = null;
        _technicalDetails = string.Empty;
        SetState(MainWindowState.CreatingArchive);
        using var cancellation = BeginOperation();
        try
        {
            _archiveResult = await _zipArchiveService.CreateAsync(
                destination,
                plan,
                replaceExistingDestination,
                new ContextProgress<ArchiveProgress>(OnArchiveProgress),
                cancellation.Token);
            SetState(MainWindowState.Success);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetState(MainWindowState.Cancelled);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetError(exception);
        }
        finally
        {
            EndOperation(cancellation);
        }
    }

    public void CancelActiveOperation() => _operationCancellation?.Cancel();

    public void OpenSettings()
    {
        if (IsBusy || _isSettingsViewOpen) return;
        _isSettingsViewOpen = true;
        RaisePropertyChanged(nameof(IsSettingsViewOpen));
        RaisePropertyChanged(nameof(IsMainViewOpen));
        RaisePropertyChanged(nameof(StatusText));
        UpdateCommandStates();
    }

    public void CloseSettings()
    {
        if (!_isSettingsViewOpen) return;
        _isSettingsViewOpen = false;
        RaisePropertyChanged(nameof(IsSettingsViewOpen));
        RaisePropertyChanged(nameof(IsMainViewOpen));
        RaisePropertyChanged(nameof(StatusText));
        UpdateCommandStates();
    }

    public Task OpenLicenseAsync() => OpenExternalFileAsync(
        _productAboutProvider.LicenseDocumentPath);

    public Task OpenReleasePageAsync() => OpenExternalUriAsync(
        _productAboutProvider.ReleasePageUri);

    public Task OpenSupportPageAsync() => OpenExternalUriAsync(
        _productAboutProvider.SupportPageUri);

    public Task OpenThirdPartyNoticesAsync() => OpenExternalFileAsync(
        _productAboutProvider.ThirdPartyNoticesPath);

    public async Task OpenDestinationFolderAsync()
    {
        if (_archiveResult is null || IsBusy) return;
        try
        {
            var directory = Path.GetDirectoryName(_archiveResult.DestinationPath)
                ?? throw new InvalidOperationException("The destination has no parent directory.");
            await _pathPicker.OpenFolderAsync(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetError(exception);
        }
    }

    public void Dispose()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        Groups.PropertyChanged -= OnGroupsPropertyChanged;
        Groups.Dispose();
        _localization.CultureChanged -= OnCultureChanged;
    }

    internal static string SuggestDestination(string sourcePath)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
        var sourceName = Path.GetFileName(source);
        if (string.IsNullOrWhiteSpace(sourceName)) return Path.Combine(source, "archive.zip");
        var parent = Path.GetDirectoryName(source);
        return parent is null ? Path.Combine(source, "archive.zip") : Path.Combine(parent, $"{sourceName}.zip");
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private bool CanChangeSource() => !IsBusy && !_isDialogOpen;

    private async Task ScanSourceAsync()
    {
        var scannedSource = _sourcePath;
        InvalidateScan();
        _scanProgressFiles = 0;
        _technicalDetails = string.Empty;
        SetState(MainWindowState.Scanning);
        using var cancellation = BeginOperation();
        try
        {
            var result = await _folderScanner.ScanAsync(
                scannedSource,
                new ContextProgress<long>(count =>
                {
                    _scanProgressFiles = count;
                    RaisePropertyChanged(nameof(StatusText));
                }),
                cancellation.Token);
            if (!string.Equals(scannedSource, _sourcePath, PathComparison)) return;
            _scanResult = result;
            Groups.Load(result);
            SetState(result.Files.Count == 0
                ? MainWindowState.EmptyResult
                : result.Warnings.Count > 0 ? MainWindowState.ResultsWithWarnings : MainWindowState.Results);
            RaiseScanSummaryChanged();
            UpdatePlanPreview();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetState(MainWindowState.Cancelled);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetError(exception);
        }
        finally
        {
            EndOperation(cancellation);
        }
    }

    private CancellationTokenSource BeginOperation()
    {
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        UpdateCommandStates();
        return _operationCancellation;
    }

    private void EndOperation(CancellationTokenSource operation)
    {
        if (ReferenceEquals(_operationCancellation, operation)) _operationCancellation = null;
        UpdateCommandStates();
    }

    private void InvalidateScan()
    {
        _scanResult = null;
        _archiveResult = null;
        _plannedCollisionCount = 0;
        Groups.Load(new ScanResult([], [], [], TimeSpan.Zero));
        RaiseScanSummaryChanged();
    }

    private void SetArchiveMode(ArchiveMode mode)
    {
        if (_archiveMode == mode || IsBusy) return;
        _archiveMode = mode;
        RaisePropertyChanged(nameof(PreserveStructure));
        RaisePropertyChanged(nameof(Flatten));
        UpdatePlanPreview();
    }

    private ArchivePlan CreateCurrentPlan()
    {
        if (_scanResult is null) throw new InvalidOperationException("A completed scan is required.");
        var selectedKeys = new HashSet<string>(
            Groups.AllRows.Where(row => row.IsSelected).Select(row => row.ExtensionKey),
            StringComparer.OrdinalIgnoreCase);
        return _archivePlanner.CreatePlan(new ArchiveRequest(
            _sourcePath, _outputPath, selectedKeys, _archiveMode, _scanResult.Files));
    }

    private void UpdatePlanPreview()
    {
        _plannedCollisionCount = 0;
        if (_scanResult is not null && Groups.SelectedGroupCount > 0 && IsValidDestination(_outputPath))
        {
            try { _plannedCollisionCount = CreateCurrentPlan().CollisionRenameCount; }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException) { }
        }

        RaisePropertyChanged(nameof(SelectionSummary));
        RaisePropertyChanged(nameof(CanCreateArchive));
        UpdateCommandStates();
    }

    private void OnArchiveProgress(ArchiveProgress progress)
    {
        _archiveCompletedFiles = progress.CompletedFiles;
        _archiveTotalFiles = progress.TotalFiles;
        _archiveCompletedBytes = progress.CompletedBytes;
        _archiveTotalBytes = progress.TotalBytes;
        RaisePropertyChanged(nameof(StatusText));
        RaisePropertyChanged(nameof(ArchiveProgressMaximum));
        RaisePropertyChanged(nameof(ArchiveProgressValue));
    }

    private void SetState(MainWindowState state)
    {
        _state = state;
        RaisePropertyChanged(nameof(State));
        RaisePropertyChanged(nameof(StatusText));
        RaisePropertyChanged(nameof(IsBusy));
        RaisePropertyChanged(nameof(IsScanning));
        RaisePropertyChanged(nameof(IsCreatingArchive));
        RaisePropertyChanged(nameof(IsProgressVisible));
        RaisePropertyChanged(nameof(ShowsEmptyState));
        RaisePropertyChanged(nameof(IsSuccess));
        RaisePropertyChanged(nameof(IsError));
        RaisePropertyChanged(nameof(CanEditArchiveOptions));
        RaisePropertyChanged(nameof(CanCreateArchive));
        RaisePropertyChanged(nameof(SuccessSummary));
        UpdateCommandStates();
    }

    private void SetError(Exception exception)
    {
        _technicalDetails = exception.ToString();
        RaisePropertyChanged(nameof(TechnicalDetails));
        SetState(MainWindowState.Error);
    }

    private void RaiseScanSummaryChanged()
    {
        RaisePropertyChanged(nameof(HasScanSummary));
        RaisePropertyChanged(nameof(HasResults));
        RaisePropertyChanged(nameof(ShowsEmptyState));
        RaisePropertyChanged(nameof(HasWarnings));
        RaisePropertyChanged(nameof(WarningSummary));
        RaisePropertyChanged(nameof(TotalFilesDisplay));
        RaisePropertyChanged(nameof(TotalGroupsDisplay));
        RaisePropertyChanged(nameof(TotalBytesDisplay));
        RaisePropertyChanged(nameof(CanEditArchiveOptions));
        RaisePropertyChanged(nameof(CanCreateArchive));
    }

    private void OnGroupsPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(ExtensionGroupListViewModel.FilterText))
        {
            RaisePropertyChanged(nameof(FilterText));
            RaisePropertyChanged(nameof(IsFilterClearVisible));
            ClearFilterCommand.RaiseCanExecuteChanged();
        }
        if (eventArgs.PropertyName == nameof(ExtensionGroupListViewModel.SelectedGroupCount))
        {
            UpdatePlanPreview();
        }
        if (eventArgs.PropertyName == nameof(ExtensionGroupListViewModel.SelectionState))
        {
            RaisePropertyChanged(nameof(AllGroupsSelectionState));
            RaisePropertyChanged(nameof(SelectionToggleAccessibleName));
            RaisePropertyChanged(nameof(SelectionToggleToolTip));
        }
        if (eventArgs.PropertyName is nameof(ExtensionGroupListViewModel.SortColumn)
            or nameof(ExtensionGroupListViewModel.SortDirection))
        {
            RaisePropertyChanged(nameof(ExtensionHeader));
            RaisePropertyChanged(nameof(FileCountHeader));
            RaisePropertyChanged(nameof(SizeHeader));
            RaisePropertyChanged(nameof(IsExtensionSortAscending));
            RaisePropertyChanged(nameof(IsExtensionSortDescending));
            RaisePropertyChanged(nameof(IsFileCountSortAscending));
            RaisePropertyChanged(nameof(IsFileCountSortDescending));
            RaisePropertyChanged(nameof(IsSizeSortAscending));
            RaisePropertyChanged(nameof(IsSizeSortDescending));
            RaisePropertyChanged(nameof(ExtensionHeaderAccessibleName));
            RaisePropertyChanged(nameof(FileCountHeaderAccessibleName));
            RaisePropertyChanged(nameof(SizeHeaderAccessibleName));
        }
    }

    private void ClearFilter()
    {
        FilterText = string.Empty;
    }

    private void OnCultureChanged(object? sender, EventArgs eventArgs)
    {
        RebuildSettingOptions();
        RaisePropertyChanged(string.Empty);
    }

    private void RebuildSettingOptions()
    {
        if (LanguageOptions.Count == 0)
        {
            LanguageOptions =
            [
                new(LanguageMode.System, _localization["LanguageSystem"]),
                .. LanguageCatalog.ProductionLanguages.Select(definition =>
                    new LanguageOption(
                        definition.Mode,
                        _localization[definition.DisplayNameResourceKey])),
            ];
            ThemeOptions =
            [
                new(ThemeMode.System, _localization["ThemeSystem"]),
                new(ThemeMode.Light, _localization["ThemeLight"]),
                new(ThemeMode.Dark, _localization["ThemeDark"]),
            ];
            RaisePropertyChanged(nameof(LanguageOptions));
            RaisePropertyChanged(nameof(ThemeOptions));
        }
        else
        {
            UpdateOptionDisplayName(LanguageMode.System, "LanguageSystem");
            foreach (var definition in LanguageCatalog.ProductionLanguages)
            {
                UpdateOptionDisplayName(definition.Mode, definition.DisplayNameResourceKey);
            }
            UpdateOptionDisplayName(ThemeMode.System, "ThemeSystem");
            UpdateOptionDisplayName(ThemeMode.Light, "ThemeLight");
            UpdateOptionDisplayName(ThemeMode.Dark, "ThemeDark");
        }

        _selectedLanguageOption = LanguageOptions.Single(option => option.Value == _settings.Language);
        _selectedThemeOption = ThemeOptions.Single(option => option.Value == _settings.Theme);
        RaisePropertyChanged(nameof(SelectedLanguageOption));
        RaisePropertyChanged(nameof(SelectedThemeOption));
    }

    private void UpdateOptionDisplayName(LanguageMode value, string resourceKey) =>
        LanguageOptions.Single(option => option.Value == value).UpdateDisplayName(_localization[resourceKey]);

    private void UpdateOptionDisplayName(ThemeMode value, string resourceKey) =>
        ThemeOptions.Single(option => option.Value == value).UpdateDisplayName(_localization[resourceKey]);

    private void QueueSettingsSave() => _ = SaveSettingsAsync();

    private async Task SaveSettingsAsync()
    {
        await _settingsSaveLock.WaitAsync().ConfigureAwait(true);
        try
        {
            await _settingsService.SaveAsync(_settings).ConfigureAwait(true);
            _settingsWarning = string.Empty;
            RaisePropertyChanged(nameof(SettingsWarning));
            RaisePropertyChanged(nameof(HasSettingsWarning));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _technicalDetails = exception.ToString();
            _settingsWarning = _localization["SettingsSaveWarning"];
            RaisePropertyChanged(nameof(TechnicalDetails));
            RaisePropertyChanged(nameof(SettingsWarning));
            RaisePropertyChanged(nameof(HasSettingsWarning));
        }
        finally
        {
            _settingsSaveLock.Release();
        }
    }

    private void UpdateCommandStates()
    {
        ChooseFolderCommand.RaiseCanExecuteChanged();
        ScanAgainCommand.RaiseCanExecuteChanged();
        BrowseOutputCommand.RaiseCanExecuteChanged();
        CreateArchiveCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        OpenDestinationFolderCommand.RaiseCanExecuteChanged();
        ToggleAllSelectionCommand.RaiseCanExecuteChanged();
        OpenSettingsCommand.RaiseCanExecuteChanged();
        CloseSettingsCommand.RaiseCanExecuteChanged();
        OpenLicenseCommand.RaiseCanExecuteChanged();
        OpenReleasePageCommand.RaiseCanExecuteChanged();
        OpenSupportPageCommand.RaiseCanExecuteChanged();
        OpenThirdPartyNoticesCommand.RaiseCanExecuteChanged();
        SortByExtensionCommand.RaiseCanExecuteChanged();
        SortByFileCountCommand.RaiseCanExecuteChanged();
        SortBySizeCommand.RaiseCanExecuteChanged();
    }

    private bool IsSort(ExtensionSortColumn column, SortDirection direction) =>
        Groups.SortColumn == column && Groups.SortDirection == direction;

    private string BuildSortAccessibleName(string resourceKey, ExtensionSortColumn column)
    {
        var label = _localization[resourceKey];
        if (Groups.SortColumn != column) return label;
        var direction = Groups.SortDirection == SortDirection.Ascending
            ? _localization["SortAscending"] : _localization["SortDescending"];
        return $"{label} {direction}";
    }

    private string Format(string key, params object[] arguments) =>
        string.Format(_localization.CurrentCulture, _localization[key], arguments);

    private string UnavailableValue => _localization["UnavailableValue"];

    private string FormatAboutValue(string label, string value) =>
        Format("AboutValueAccessibleNameFormat", label, value);

    private async Task OpenExternalFileAsync(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            await _externalResourceService.OpenFileAsync(path);
        }
        catch (Exception exception) when (IsExternalResourceException(exception))
        {
            SetExternalResourceWarning(exception);
        }
    }

    private async Task OpenExternalUriAsync(Uri? uri)
    {
        if (uri is null)
        {
            return;
        }

        try
        {
            await _externalResourceService.OpenUriAsync(uri);
        }
        catch (Exception exception) when (IsExternalResourceException(exception))
        {
            SetExternalResourceWarning(exception);
        }
    }

    private void SetExternalResourceWarning(Exception exception)
    {
        _technicalDetails = exception.ToString();
        _settingsWarning = _localization["ExternalResourceOpenWarning"];
        RaisePropertyChanged(nameof(TechnicalDetails));
        RaisePropertyChanged(nameof(SettingsWarning));
        RaisePropertyChanged(nameof(HasSettingsWarning));
    }

    private static bool IsExternalResourceException(Exception exception) => exception is
        IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
        or Win32Exception;

    private static bool IsValidDestination(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            return directory is not null && Directory.Exists(directory);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string EnsureZipExtension(string path) =>
        string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase) ? path : $"{path}.zip";
}
