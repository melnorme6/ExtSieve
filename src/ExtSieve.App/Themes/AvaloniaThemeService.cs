using Avalonia;
using Avalonia.Styling;
using ExtSieve.App.Settings;

namespace ExtSieve.App.Themes;

public sealed class AvaloniaThemeService(Application application) : IThemeService
{
    private readonly Application _application = application ?? throw new ArgumentNullException(nameof(application));

    public void Apply(ThemeMode mode)
    {
        _application.RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
