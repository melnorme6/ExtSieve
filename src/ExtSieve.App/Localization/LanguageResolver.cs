using System.Globalization;
using ExtSieve.App.Settings;

namespace ExtSieve.App.Localization;

public static class LanguageResolver
{
    public static CultureInfo Resolve(LanguageMode mode, CultureInfo? systemCulture = null)
    {
        if (mode == LanguageMode.System)
        {
            return ResolveSystemCulture(systemCulture ?? CultureInfo.CurrentUICulture);
        }

        var definition = LanguageCatalog.Find(mode) ?? LanguageCatalog.English;
        return CultureInfo.GetCultureInfo(definition.CultureName);
    }

    private static CultureInfo ResolveSystemCulture(CultureInfo culture)
    {
        var definition = LanguageCatalog.FindForSystemCulture(culture) ?? LanguageCatalog.English;
        return CultureInfo.GetCultureInfo(definition.CultureName);
    }
}
