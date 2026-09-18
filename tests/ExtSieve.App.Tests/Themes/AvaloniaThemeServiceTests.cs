using System.Xml.Linq;
using Avalonia;
using Avalonia.Styling;
using ExtSieve.App.Settings;
using ExtSieve.App.Themes;

namespace ExtSieve.App.Tests.Themes;

public sealed class AvaloniaThemeServiceTests
{
    [Fact]
    public void ApplyMapsEveryThemeModeImmediately()
    {
        var application = new Application();
        var service = new AvaloniaThemeService(application);

        service.Apply(ThemeMode.Dark);
        Assert.Same(ThemeVariant.Dark, application.RequestedThemeVariant);

        service.Apply(ThemeMode.Light);
        Assert.Same(ThemeVariant.Light, application.RequestedThemeVariant);

        service.Apply(ThemeMode.System);
        Assert.Same(ThemeVariant.Default, application.RequestedThemeVariant);
    }

    [Fact]
    public void SemanticThemeResourcesCoverInteractiveControlStates()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "ExtSieve.App", "Themes", "SemanticColors.axaml"));
        var document = XDocument.Load(sourcePath);
        XNamespace presentation = "https://github.com/avaloniaui";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string[] requiredKeys =
        [
            "WindowBackgroundBrush",
            "SurfaceBrush",
            "InputSurfaceBrush",
            "StatusBarBrush",
            "SecondaryControlBrush",
            "SecondaryControlHoverBrush",
            "SecondaryControlPressedBrush",
            "AlternateRowBrush",
            "RowHoverBrush",
            "SelectionBrush",
            "SelectionHoverBrush",
            "FocusBrush",
            "DisabledTextBrush",
            "DisabledSurfaceBrush",
            "DisabledBorderBrush",
        ];

        foreach (var themeName in new[] { "Light", "Dark" })
        {
            var theme = document.Descendants(presentation + "ResourceDictionary")
                .Single(element => (string?)element.Attribute(x + "Key") == themeName);
            var keys = theme.Elements()
                .Select(element => (string?)element.Attribute(x + "Key"))
                .Where(key => key is not null)
                .ToHashSet(StringComparer.Ordinal);

            Assert.All(requiredKeys, key => Assert.Contains(key, keys));
        }
    }

    [Fact]
    public void PrimaryActionStatesMeetNormalTextContrastInEveryTheme()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "ExtSieve.App", "Themes", "SemanticColors.axaml"));
        var document = XDocument.Load(sourcePath);
        XNamespace presentation = "https://github.com/avaloniaui";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string[] primaryActionBackgrounds =
        [
            "AccentStrongBrush",
            "AccentHoverBrush",
            "AccentPressedBrush",
        ];

        foreach (var themeName in new[] { "Light", "Dark" })
        {
            var theme = document.Descendants(presentation + "ResourceDictionary")
                .Single(element => (string?)element.Attribute(x + "Key") == themeName);

            foreach (var resourceKey in primaryActionBackgrounds)
            {
                var background = theme.Elements()
                    .Single(element => (string?)element.Attribute(x + "Key") == resourceKey)
                    .Value;
                var contrast = ContrastRatio(background, "#FFFFFF");

                Assert.True(
                    contrast >= 4.5,
                    $"{themeName} {resourceKey} contrast was {contrast:F2}:1.");
            }

            Assert.Equal("#FFFFFF", GetBrushValue(theme, x, "AccentTextBrush"));

            Assert.NotEqual(
                GetBrushValue(theme, x, "AccentStrongBrush"),
                GetBrushValue(theme, x, "DisabledSurfaceBrush"));
            Assert.NotEqual("#FFFFFF", GetBrushValue(theme, x, "DisabledTextBrush"));
        }
    }

    private static string GetBrushValue(XElement theme, XNamespace x, string resourceKey) =>
        theme.Elements()
            .Single(element => (string?)element.Attribute(x + "Key") == resourceKey)
            .Value;

    private static double ContrastRatio(string first, string second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05)
            / (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double RelativeLuminance(string color)
    {
        var channels = new[]
        {
            Convert.ToInt32(color[1..3], 16) / 255d,
            Convert.ToInt32(color[3..5], 16) / 255d,
            Convert.ToInt32(color[5..7], 16) / 255d,
        };
        var linear = channels.Select(channel => channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4)).ToArray();
        return (0.2126 * linear[0]) + (0.7152 * linear[1]) + (0.0722 * linear[2]);
    }
}
