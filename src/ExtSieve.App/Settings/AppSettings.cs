namespace ExtSieve.App.Settings;

public enum LanguageMode
{
    System,
    English,
    Italian,
    German,
    French,
    Spanish,
    PortugueseBrazil,
}

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

public sealed record AppSettings
{
    public LanguageMode Language { get; init; } = LanguageMode.System;

    public ThemeMode Theme { get; init; } = ThemeMode.System;
}
