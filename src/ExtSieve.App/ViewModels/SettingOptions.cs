using ExtSieve.App.Settings;

namespace ExtSieve.App.ViewModels;

public sealed class LanguageOption(LanguageMode value, string displayName) : ObservableObject
{
    public LanguageMode Value { get; } = value;

    public string DisplayName { get; private set; } = displayName;

    public void UpdateDisplayName(string displayName)
    {
        if (string.Equals(DisplayName, displayName, StringComparison.Ordinal)) return;
        DisplayName = displayName;
        RaisePropertyChanged(nameof(DisplayName));
    }
}

public sealed class ThemeOption(ThemeMode value, string displayName) : ObservableObject
{
    public ThemeMode Value { get; } = value;

    public string DisplayName { get; private set; } = displayName;

    public void UpdateDisplayName(string displayName)
    {
        if (string.Equals(DisplayName, displayName, StringComparison.Ordinal)) return;
        DisplayName = displayName;
        RaisePropertyChanged(nameof(DisplayName));
    }
}
