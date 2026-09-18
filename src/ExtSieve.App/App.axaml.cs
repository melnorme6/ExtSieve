using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
                new AssemblyProductAboutProvider(applicationAssembly),
                new ShellExternalResourceService());
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
