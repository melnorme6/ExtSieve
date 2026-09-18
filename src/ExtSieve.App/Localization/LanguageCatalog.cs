using System.Globalization;
using ExtSieve.App.Settings;

namespace ExtSieve.App.Localization;

public sealed record LanguageDefinition(
    LanguageMode Mode,
    string CultureName,
    string DisplayNameResourceKey,
    bool MapAllRegionalCultures);

public static class LanguageCatalog
{
    public static IReadOnlyList<LanguageDefinition> ProductionLanguages { get; } =
    [
        new(LanguageMode.English, "en", "LanguageEnglish", true),
        new(LanguageMode.Italian, "it", "LanguageItalian", true),
        new(LanguageMode.German, "de", "LanguageGerman", true),
        new(LanguageMode.French, "fr", "LanguageFrench", true),
        new(LanguageMode.Spanish, "es", "LanguageSpanish", true),
        new(LanguageMode.PortugueseBrazil, "pt-BR", "LanguagePortugueseBrazil", false),
    ];

    public static LanguageDefinition English => ProductionLanguages[0];

    public static LanguageDefinition? Find(LanguageMode mode) =>
        ProductionLanguages.SingleOrDefault(definition => definition.Mode == mode);

    public static LanguageDefinition? FindForSystemCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return ProductionLanguages.FirstOrDefault(definition =>
            definition.MapAllRegionalCultures
                ? string.Equals(
                    CultureInfo.GetCultureInfo(definition.CultureName).TwoLetterISOLanguageName,
                    culture.TwoLetterISOLanguageName,
                    StringComparison.OrdinalIgnoreCase)
                : string.Equals(
                    definition.CultureName,
                    culture.Name,
                    StringComparison.OrdinalIgnoreCase));
    }
}
