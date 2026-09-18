using ExtSieve.App.Localization;
using ExtSieve.App.Services;
using ExtSieve.App.Settings;
using ExtSieve.App.Themes;
using ExtSieve.App.ViewModels;
using ExtSieve.Core.Models;
using ExtSieve.Core.Services;

namespace ExtSieve.App.Tests.Support;

internal sealed class MainWindowFixture : IDisposable
{
    public MainWindowFixture(LanguageMode language = LanguageMode.English)
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"ExtSieve.App.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootPath);
        Localization = new LocalizationService(language);
        Scanner = new StubFolderScanner();
        Zip = new StubZipArchiveService();
        Picker = new StubPathPickerService();
        Prompt = new StubPromptService();
        Settings = new StubSettingsService();
        Theme = new StubThemeService();
        ProductVersion = new StubProductVersionProvider();
        ProductAbout = new StubProductAboutProvider();
        ExternalResources = new StubExternalResourceService();
        Planner = new StubArchivePlanner();
        ViewModel = new MainWindowViewModel(
            Localization,
            Scanner,
            Planner,
            Zip,
            Picker,
            Prompt,
            Settings,
            Theme,
            ProductVersion,
            ProductAbout,
            ExternalResources);
    }

    public string RootPath { get; }
    public LocalizationService Localization { get; }
    public StubFolderScanner Scanner { get; }
    public StubZipArchiveService Zip { get; }
    public StubPathPickerService Picker { get; }
    public StubPromptService Prompt { get; }
    public StubSettingsService Settings { get; }
    public StubThemeService Theme { get; }
    public StubProductVersionProvider ProductVersion { get; }
    public StubProductAboutProvider ProductAbout { get; }
    public StubExternalResourceService ExternalResources { get; }
    public StubArchivePlanner Planner { get; }
    public MainWindowViewModel ViewModel { get; }

    public ScannedFile CreateScannedFile(string relativePath, byte[]? content = null)
    {
        var absolutePath = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllBytes(absolutePath, content ?? "content"u8.ToArray());
        var info = new FileInfo(absolutePath);
        return new ScannedFile(
            absolutePath,
            relativePath.Replace('\\', '/'),
            Path.GetExtension(relativePath).ToLowerInvariant(),
            info.Length,
            info.LastWriteTimeUtc);
    }

    public static ScanResult Result(params ScannedFile[] files)
    {
        var groups = files
            .GroupBy(file => file.ExtensionKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ExtensionGroup(
                group.Key,
                group.LongCount(),
                group.Sum(file => file.Size)))
            .ToArray();
        return new ScanResult(files, groups, [], TimeSpan.Zero);
    }

    public void Dispose()
    {
        ViewModel.Dispose();
        Directory.Delete(RootPath, recursive: true);
    }
}

internal sealed class StubArchivePlanner : IArchivePlanner
{
    private readonly ArchivePlanner _inner = new();

    public int CallCount { get; private set; }

    public ArchivePlan CreatePlan(ArchiveRequest request)
    {
        CallCount++;
        return _inner.CreatePlan(request);
    }
}

internal sealed class StubFolderScanner : IFolderScanner
{
    public Func<string, IProgress<long>?, CancellationToken, Task<ScanResult>> Handler { get; set; } =
        (_, _, _) => Task.FromResult(new ScanResult([], [], [], TimeSpan.Zero));

    public int CallCount { get; private set; }

    public Task<ScanResult> ScanAsync(
        string sourceDirectory,
        IProgress<long>? discoveredFileProgress,
        CancellationToken cancellationToken)
    {
        CallCount++;
        return Handler(sourceDirectory, discoveredFileProgress, cancellationToken);
    }
}

internal sealed class StubZipArchiveService : IZipArchiveService
{
    public Func<string, ArchivePlan, bool, IProgress<ArchiveProgress>?, CancellationToken, Task<ArchiveResult>> Handler { get; set; } =
        (destination, plan, _, progress, _) =>
        {
            progress?.Report(new ArchiveProgress(
                plan.Entries.Count,
                plan.Entries.Count,
                plan.TotalSourceBytes,
                plan.TotalSourceBytes));
            return Task.FromResult(new ArchiveResult(
                destination,
                plan.Entries.Count,
                plan.TotalSourceBytes,
                100,
                TimeSpan.FromSeconds(1)));
        };

    public int CallCount { get; private set; }

    public bool? LastReplaceExistingDestination { get; private set; }

    public Task<ArchiveResult> CreateAsync(
        string destinationPath,
        ArchivePlan plan,
        bool replaceExistingDestination,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastReplaceExistingDestination = replaceExistingDestination;
        return Handler(
            destinationPath,
            plan,
            replaceExistingDestination,
            progress,
            cancellationToken);
    }
}

internal sealed class StubPathPickerService : IPathPickerService
{
    public string? SourcePath { get; set; }
    public string? DestinationPath { get; set; }
    public string? OpenedFolder { get; private set; }

    public Task<string?> PickSourceFolderAsync(string title) => Task.FromResult(SourcePath);

    public Task<string?> PickArchiveDestinationAsync(string title, string suggestedFileName) =>
        Task.FromResult(DestinationPath);

    public Task OpenFolderAsync(string folderPath)
    {
        OpenedFolder = folderPath;
        return Task.CompletedTask;
    }
}

internal sealed class StubPromptService : IUserPromptService
{
    public bool Response { get; set; } = true;
    public int CallCount { get; private set; }
    public string? LastTitle { get; private set; }
    public string? LastMessage { get; private set; }
    public string? LastConfirmLabel { get; private set; }
    public string? LastCancelLabel { get; private set; }

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, string cancelLabel)
    {
        CallCount++;
        LastTitle = title;
        LastMessage = message;
        LastConfirmLabel = confirmLabel;
        LastCancelLabel = cancelLabel;
        return Task.FromResult(Response);
    }
}

internal sealed class StubSettingsService : ISettingsService
{
    public AppSettings Settings { get; set; } = new();
    public List<AppSettings> SavedSettings { get; } = [];
    public Exception? SaveException { get; set; }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Settings);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (SaveException is not null)
        {
            return Task.FromException(SaveException);
        }

        SavedSettings.Add(settings);
        return Task.CompletedTask;
    }
}

internal sealed class StubThemeService : IThemeService
{
    public List<ThemeMode> AppliedModes { get; } = [];

    public void Apply(ThemeMode mode) => AppliedModes.Add(mode);
}

internal sealed class StubProductVersionProvider : IProductVersionProvider
{
    public string PublicVersion { get; set; } = "1.0.0";
}

internal sealed class StubProductAboutProvider : IProductAboutProvider
{
    public string ProductName { get; set; } = "ExtSieve";
    public string? BuildInfo { get; set; }
    public string? LicenseName { get; set; } = "MIT License";
    public string? LicenseDocumentPath { get; set; }
    public Uri? ReleasePageUri { get; set; }
    public Uri? SupportPageUri { get; set; }
    public string? ThirdPartyNoticesPath { get; set; }
}

internal sealed class StubExternalResourceService : IExternalResourceService
{
    public List<string> OpenedFiles { get; } = [];
    public List<Uri> OpenedUris { get; } = [];

    public Task OpenFileAsync(string filePath)
    {
        OpenedFiles.Add(filePath);
        return Task.CompletedTask;
    }

    public Task OpenUriAsync(Uri uri)
    {
        OpenedUris.Add(uri);
        return Task.CompletedTask;
    }
}
