using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ExtSieve.App.Localization;
using ExtSieve.App.Settings;
using ExtSieve.App.Tests.Support;
using ExtSieve.App.ViewModels;

namespace ExtSieve.App.Tests.Localization;

public sealed partial class LocalizationTests
{
    private static readonly (LanguageMode Mode, string CultureName, string ResourceFile)[] Languages =
    [
        (LanguageMode.English, "en", "Strings.resx"),
        (LanguageMode.Italian, "it", "Strings.it.resx"),
        (LanguageMode.German, "de", "Strings.de.resx"),
        (LanguageMode.French, "fr", "Strings.fr.resx"),
        (LanguageMode.Spanish, "es", "Strings.es.resx"),
        (LanguageMode.PortugueseBrazil, "pt-BR", "Strings.pt-BR.resx"),
    ];

    [Theory]
    [InlineData(LanguageMode.English)]
    [InlineData(LanguageMode.Italian)]
    [InlineData(LanguageMode.German)]
    [InlineData(LanguageMode.French)]
    [InlineData(LanguageMode.Spanish)]
    [InlineData(LanguageMode.PortugueseBrazil)]
    public void RequiredResourcesArePresentAndNonEmpty(LanguageMode mode)
    {
        var localization = new LocalizationService(mode);

        foreach (var key in LocalizationCatalog.RequiredKeys)
        {
            Assert.False(string.IsNullOrWhiteSpace(localization[key]));
            Assert.True(localization.HasTranslation(key));
        }
    }

    [Fact]
    public void EveryProductionResourceHasExactKeyParity()
    {
        var expected = LocalizationCatalog.RequiredKeys.ToHashSet(StringComparer.Ordinal);
        Assert.Equal(LocalizationCatalog.RequiredKeys.Count, expected.Count);

        foreach (var (_, _, resourceFile) in Languages)
        {
            var values = LoadResource(resourceFile);
            Assert.Equal(expected.Count, values.Count);
            Assert.True(expected.SetEquals(values.Keys), resourceFile);
            Assert.All(values, pair => Assert.False(
                string.IsNullOrWhiteSpace(pair.Value),
                $"{resourceFile}:{pair.Key}"));
        }
    }

    [Fact]
    public void TranslatedResourcesContainNoUnexpectedEnglishValues()
    {
        var english = LoadResource("Strings.resx");
        var commonIntentionalMatches = new HashSet<string>(StringComparer.Ordinal)
        {
            "AppTitle",
            "LanguageEnglish",
            "LanguageItalian",
            "LanguageGerman",
            "LanguageFrench",
            "LanguageSpanish",
            "LanguagePortugueseBrazil",
            "UnavailableValue",
            "AboutValueAccessibleNameFormat",
        };
        var localeSpecificMatches = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Strings.de.resx"] = ["LanguageSystem", "ThemeSystem", "AboutName", "AboutVersion"],
            ["Strings.fr.resx"] = ["Extensions", "Extension", "AboutVersion"],
            ["Strings.it.resx"] = [],
            ["Strings.es.resx"] = [],
            ["Strings.pt-BR.resx"] = [],
        };

        foreach (var (resourceFile, additionalMatches) in localeSpecificMatches)
        {
            var allowed = commonIntentionalMatches
                .Concat(additionalMatches)
                .ToHashSet(StringComparer.Ordinal);
            var translated = LoadResource(resourceFile);
            var matchingKeys = translated
                .Where(pair => string.Equals(pair.Value, english[pair.Key], StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .ToHashSet(StringComparer.Ordinal);

            Assert.True(allowed.SetEquals(matchingKeys), resourceFile);
        }
    }

    [Fact]
    public void CompositeFormatPlaceholdersMatchEnglishInEveryLanguage()
    {
        var english = LoadResource("Strings.resx");

        foreach (var (_, _, resourceFile) in Languages.Skip(1))
        {
            var translated = LoadResource(resourceFile);
            foreach (var key in english.Keys)
            {
                Assert.Equal(
                    Placeholders().Matches(english[key]).Select(match => match.Value),
                    Placeholders().Matches(translated[key]).Select(match => match.Value));
            }
        }
    }

    [Theory]
    [InlineData("en-US", "en")]
    [InlineData("it-IT", "it")]
    [InlineData("de-DE", "de")]
    [InlineData("de-AT", "de")]
    [InlineData("de-CH", "de")]
    [InlineData("fr-FR", "fr")]
    [InlineData("fr-CA", "fr")]
    [InlineData("es-ES", "es")]
    [InlineData("es-MX", "es")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("pt-PT", "en")]
    [InlineData("nl-NL", "en")]
    public void SystemLanguageResolvesSupportedCultureOrEnglishFallback(
        string systemCulture,
        string expectedCulture)
    {
        var resolved = LanguageResolver.Resolve(
            LanguageMode.System,
            CultureInfo.GetCultureInfo(systemCulture));

        Assert.Equal(expectedCulture, resolved.Name);
    }

    [Fact]
    public void ProductionLanguageRegistryHasApprovedOrderCulturesAndAutonyms()
    {
        using var fixture = new MainWindowFixture();
        var options = fixture.ViewModel.LanguageOptions;

        Assert.Equal(
            [
                LanguageMode.System,
                LanguageMode.English,
                LanguageMode.Italian,
                LanguageMode.German,
                LanguageMode.French,
                LanguageMode.Spanish,
                LanguageMode.PortugueseBrazil,
            ],
            options.Select(option => option.Value));
        Assert.Equal(
            ["English", "Italiano", "Deutsch", "Français", "Español", "Português (Brasil)"],
            options.Skip(1).Select(option => option.DisplayName));
        Assert.Equal(
            Languages.Select(language => language.CultureName),
            LanguageCatalog.ProductionLanguages.Select(language => language.CultureName));
    }

    [Fact]
    public void RepeatedLanguageChangesUpdateVisibleStateWithoutChangingThemeOrAutonyms()
    {
        using var fixture = new MainWindowFixture();
        fixture.ViewModel.SelectedThemeOption = fixture.ViewModel.ThemeOptions
            .Single(option => option.Value == ThemeMode.Dark);
        LanguageMode[] sequence =
        [
            LanguageMode.Italian,
            LanguageMode.German,
            LanguageMode.French,
            LanguageMode.Spanish,
            LanguageMode.PortugueseBrazil,
            LanguageMode.English,
            LanguageMode.French,
            LanguageMode.English,
        ];

        foreach (var mode in sequence)
        {
            fixture.ViewModel.SelectedLanguageOption = fixture.ViewModel.LanguageOptions
                .Single(option => option.Value == mode);
            var expected = Languages.Single(language => language.Mode == mode);

            Assert.Equal(expected.CultureName, fixture.Localization.CurrentCulture.Name);
            Assert.Equal(fixture.Localization["AppPurpose"], fixture.ViewModel.AppPurpose);
            Assert.Equal(fixture.Localization["ClearFilter"], fixture.ViewModel.ClearFilterLabel);
            Assert.Equal(ThemeMode.Dark, fixture.ViewModel.SelectedThemeOption.Value);
            Assert.Equal(
                ["English", "Italiano", "Deutsch", "Français", "Español", "Português (Brasil)"],
                fixture.ViewModel.LanguageOptions.Skip(1).Select(option => option.DisplayName));
        }
    }

    [Fact]
    public void AboutConfirmationAndFilterClearStringsAreLocalizedInEveryLanguage()
    {
        var expected = new Dictionary<LanguageMode, (string About, string Replace, string Clear)>
        {
            [LanguageMode.English] = ("About", "Replace existing archive?", "Clear filter"),
            [LanguageMode.Italian] = ("Informazioni", "Sostituire l'archivio esistente?", "Cancella filtro"),
            [LanguageMode.German] = ("Info", "Vorhandenes Archiv ersetzen?", "Filter löschen"),
            [LanguageMode.French] = ("À propos", "Remplacer l’archive existante ?", "Effacer le filtre"),
            [LanguageMode.Spanish] = ("Acerca de", "¿Reemplazar el archivo existente?", "Borrar filtro"),
            [LanguageMode.PortugueseBrazil] = ("Sobre", "Substituir o arquivo existente?", "Limpar filtro"),
        };

        foreach (var (mode, values) in expected)
        {
            var localization = new LocalizationService(mode);
            Assert.Equal(values.About, localization["About"]);
            Assert.Equal(values.Replace, localization["ReplaceArchiveTitle"]);
            Assert.Equal(values.Clear, localization["ClearFilter"]);
        }
    }

    [Theory]
    [InlineData(LanguageMode.English, "1.5 KiB")]
    [InlineData(LanguageMode.Italian, "1,5 KiB")]
    [InlineData(LanguageMode.German, "1,5 KiB")]
    [InlineData(LanguageMode.French, "1,5 KiB")]
    [InlineData(LanguageMode.Spanish, "1,5 KiB")]
    [InlineData(LanguageMode.PortugueseBrazil, "1,5 KiB")]
    public void BinarySizesUseActiveCultureAndInvariantBinaryUnits(LanguageMode mode, string expected)
    {
        var localization = new LocalizationService(mode);
        Assert.Equal(expected, ByteSizeFormatter.Format(1536, localization.CurrentCulture));
    }

    [Fact]
    public void LocalizedSummariesUseEachActiveCultureNumberFormat()
    {
        foreach (var (mode, _, _) in Languages)
        {
            var localization = new LocalizationService(mode);
            var summary = string.Format(
                localization.CurrentCulture,
                localization["SelectionSummaryFormat"],
                1234,
                1234,
                ByteSizeFormatter.Format(1536, localization.CurrentCulture),
                1234);
            var formattedCount = 1234.ToString("N0", localization.CurrentCulture);

            Assert.Contains(formattedCount, summary, StringComparison.Ordinal);
            Assert.Contains("KiB", summary, StringComparison.Ordinal);
        }
    }

    private static Dictionary<string, string> LoadResource(string fileName)
    {
        var document = XDocument.Load(ProductPath("src", "ExtSieve.App", "Localization", fileName));
        return document.Root!.Elements("data").ToDictionary(
            element => (string)element.Attribute("name")!,
            element => element.Element("value")!.Value,
            StringComparer.Ordinal);
    }

    private static string ProductPath(params string[] parts) => Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));

    [GeneratedRegex(@"\{\d+(?::[^}]+)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholders();
}
