using System.Globalization;
using System.Resources;
using ExtSieve.App.Settings;

namespace ExtSieve.App.Localization;

public sealed class LocalizationService
{
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en");
    private static readonly ResourceManager Resources = new(
        "ExtSieve.App.Localization.Strings",
        typeof(LocalizationService).Assembly);

    public LocalizationService(LanguageMode languageMode = LanguageMode.System, CultureInfo? systemCulture = null)
    {
        CurrentCulture = LanguageResolver.Resolve(languageMode, systemCulture);
    }

    public event EventHandler? CultureChanged;

    public CultureInfo CurrentCulture { get; private set; }

    public string this[string key]
    {
        get
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            return Resources.GetString(key, CurrentCulture)
                ?? Resources.GetString(key, EnglishCulture)
                ?? key;
        }
    }

    public bool HasTranslation(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var resourceCulture = Equals(CurrentCulture, EnglishCulture)
            ? CultureInfo.InvariantCulture
            : CurrentCulture;
        var resourceSet = Resources.GetResourceSet(
            resourceCulture,
            createIfNotExists: true,
            tryParents: false);
        return !string.IsNullOrWhiteSpace(resourceSet?.GetString(key));
    }

    public void SetLanguage(LanguageMode languageMode, CultureInfo? systemCulture = null)
    {
        var culture = LanguageResolver.Resolve(languageMode, systemCulture);
        if (Equals(CurrentCulture, culture))
        {
            return;
        }

        CurrentCulture = culture;
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}
