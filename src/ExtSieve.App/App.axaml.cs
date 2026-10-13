using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ExtSieve.App.LocalData;
using ExtSieve.App.Localization;
using ExtSieve.App.Services;
using ExtSieve.App.Settings;
using ExtSieve.App.Themes;
using ExtSieve.App.ViewModels;
using ExtSieve.Core.Services;

namespace ExtSieve.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var localization = new LocalizationService();
            var mainWindow = new MainWindow();
            var applicationAssembly = typeof(App).Assembly;
            var aboutProvider = new AssemblyProductAboutProvider(applicationAssembly);
            mainWindow.DataContext = new MainWindowViewModel(
                localization,
                new FolderScanner(),
                new ArchivePlanner(),
                new ZipArchiveService(),
                new AvaloniaPathPickerService(mainWindow),
                new WindowPromptService(mainWindow),
                new JsonSettingsService(DefaultSettingsPath.Get()),
                new AvaloniaThemeService(this),
                new AssemblyProductVersionProvider(applicationAssembly),
                aboutProvider,
                new ShellExternalResourceService(),
                new JsonProfileStore(Path.Combine(Path.GetDirectoryName(DefaultSettingsPath.Get())!, "profiles.json")),
                new JsonExclusionStore(Path.Combine(Path.GetDirectoryName(DefaultSettingsPath.Get())!, "exclusions.json")),
                new WindowFeatureDialogService(mainWindow),
                new FolderBrowser(),
                new GitHubUpdateChecker(aboutProvider.ReleasePageUri));
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
