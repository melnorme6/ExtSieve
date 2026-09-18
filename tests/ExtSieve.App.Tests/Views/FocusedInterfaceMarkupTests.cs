using System.Xml.Linq;

namespace ExtSieve.App.Tests.Views;

public sealed class FocusedInterfaceMarkupTests
{
    private static readonly XNamespace Presentation = "https://github.com/avaloniaui";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void MainWindowUsesFocusedSelectionSettingsAndSaveContract()
    {
        var document = XDocument.Load(ProductPath("src", "ExtSieve.App", "MainWindow.axaml"));
        var markup = document.ToString(SaveOptions.DisableFormatting);

        Assert.Empty(document.Descendants(Presentation + "ComboBox"));
        Assert.Empty(document.Descendants(Presentation + "PathIcon"));
        Assert.Single(document.Descendants(Presentation + "CheckBox")
, element => (string?)element.Attribute("IsThreeState") == "True");
        Assert.Contains("ToggleAllSelectionCommand", markup, StringComparison.Ordinal);
        Assert.Contains("SaveLabel", markup, StringComparison.Ordinal);
        Assert.Contains("SettingsView", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectAllCommand", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearSelectionCommand", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateZipLabel", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsViewContainsTextOnlyThemeAndLanguageControls()
    {
        var document = XDocument.Load(ProductPath(
            "src", "ExtSieve.App", "Views", "SettingsView.axaml"));

        Assert.Equal(2, document.Descendants(Presentation + "ComboBox").Count());
        Assert.All(document.Descendants(Presentation + "ComboBox"), comboBox =>
            Assert.Equal("Stretch", (string?)comboBox.Attribute("HorizontalAlignment")));
        Assert.Empty(document.Descendants(Presentation + "PathIcon"));
        Assert.Contains(document.Descendants(Presentation + "Button"), element =>
            ((string?)element.Attribute("Command"))?.Contains(
                "CloseSettingsCommand", StringComparison.Ordinal) == true);
        Assert.DoesNotContain("SettingsDescription", document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsAboutUsesTheApprovedHeadingCardAndRowOrder()
    {
        var document = XDocument.Load(ProductPath(
            "src", "ExtSieve.App", "Views", "SettingsView.axaml"));
        var aboutHeading = document.Descendants(Presentation + "TextBlock")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "AboutHeading");
        var aboutCard = document.Descendants(Presentation + "Border")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "AboutCard");
        string[] expectedLabels =
        [
            "{Binding AboutNameLabel}",
            "{Binding AboutVersionLabel}",
            "{Binding AboutBuildInfoLabel}",
            "{Binding AboutUpdatesLabel}",
            "{Binding AboutLicenseLabel}",
            "{Binding AboutReleasePageLabel}",
            "{Binding AboutSupportProjectLabel}",
            "{Binding AboutThirdPartyNoticesLabel}",
        ];
        var actualLabels = aboutCard.Descendants(Presentation + "TextBlock")
            .Select(element => (string?)element.Attribute("Text"))
            .Where(text => text is not null && text.Contains("About", StringComparison.Ordinal))
            .ToArray();

        Assert.DoesNotContain(aboutHeading.Ancestors(), ancestor => ReferenceEquals(ancestor, aboutCard));
        Assert.Equal(expectedLabels, actualLabels);
        Assert.Contains(aboutCard.Descendants(Presentation + "Button"), element =>
            (string?)element.Attribute("Command") == "{Binding OpenSupportPageCommand}");
        Assert.Single(document.Descendants(Presentation + "ScrollViewer"));
    }

    [Fact]
    public void SettingsAboutLinksKeepPointerStatesTransparent()
    {
        var document = XDocument.Load(ProductPath(
            "src", "ExtSieve.App", "Views", "SettingsView.axaml"));
        string[] transparentTemplateSelectors =
        [
            "Button.aboutLink:pointerover /template/ ContentPresenter#PART_ContentPresenter",
            "Button.aboutLink:pressed /template/ ContentPresenter#PART_ContentPresenter",
        ];

        foreach (var selector in transparentTemplateSelectors)
        {
            var style = document.Descendants(Presentation + "Style")
                .Single(element => (string?)element.Attribute("Selector") == selector);
            var setters = style.Elements(Presentation + "Setter").ToDictionary(
                element => (string)element.Attribute("Property")!,
                element => (string)element.Attribute("Value")!,
                StringComparer.Ordinal);

            Assert.Equal("Transparent", setters["Background"]);
            Assert.Equal("Transparent", setters["BorderBrush"]);
        }

        var hoverStyle = document.Descendants(Presentation + "Style")
            .Single(element => (string?)element.Attribute("Selector")
                == "Button.aboutLink:pointerover");
        Assert.Contains(hoverStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Foreground"
            && (string?)setter.Attribute("Value") == "{DynamicResource FocusBrush}");
    }

    [Fact]
    public void FooterUsesTheCompactSingleLineSizingContract()
    {
        var document = XDocument.Load(ProductPath("src", "ExtSieve.App", "MainWindow.axaml"));
        var statusStyle = document.Descendants(Presentation + "Style")
            .Single(element => (string?)element.Attribute("Selector") == "Border.statusBar");
        var iconButtonStyle = document.Descendants(Presentation + "Style")
            .Single(element => (string?)element.Attribute("Selector") == "Button.iconButton");

        Assert.Contains(statusStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Padding"
            && (string?)setter.Attribute("Value") == "16,2");
        Assert.Contains(iconButtonStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "MinHeight"
            && (string?)setter.Attribute("Value") == "28");
    }

    [Fact]
    public void PrimaryActionOverridesFluentTemplateForInteractiveStateColors()
    {
        var document = XDocument.Load(ProductPath("src", "ExtSieve.App", "MainWindow.axaml"));
        var selectors = document.Descendants(Presentation + "Style")
            .Select(element => (string?)element.Attribute("Selector"))
            .Where(selector => selector is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(
            "Button.primary:pointerover /template/ ContentPresenter#PART_ContentPresenter",
            selectors);
        Assert.Contains(
            "Button.primary:pressed /template/ ContentPresenter#PART_ContentPresenter",
            selectors);
        Assert.Contains(
            "Button.primary:disabled /template/ ContentPresenter#PART_ContentPresenter",
            selectors);
    }

    [Fact]
    public void FilterUsesAContextualLocalizedTablerClearAction()
    {
        var document = XDocument.Load(ProductPath("src", "ExtSieve.App", "MainWindow.axaml"));
        var clearButton = document.Descendants(Presentation + "Button")
            .Single(element => (string?)element.Attribute("Command") == "{Binding ClearFilterCommand}");
        var filterTextBox = document.Descendants(Presentation + "TextBox")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "FilterTextBox");

        Assert.Equal("{Binding IsFilterClearVisible}", (string?)clearButton.Attribute("IsVisible"));
        Assert.Equal("{Binding ClearFilterLabel}", (string?)clearButton.Attribute("ToolTip.Tip"));
        Assert.Equal(
            "{Binding ClearFilterLabel}",
            (string?)clearButton.Attribute("AutomationProperties.Name"));
        Assert.Contains(clearButton.Descendants().Where(element => element.Name.LocalName == "TablerIcon"), icon =>
            (string?)icon.Attribute("Data") == "{StaticResource TablerX}");
        Assert.Equal("10,5,38,5", (string?)filterTextBox.Attribute("Padding"));
    }

    [Fact]
    public void SourceAndOutputPathsRemainReadOnlySelectableTextBoxes()
    {
        var document = XDocument.Load(ProductPath("src", "ExtSieve.App", "MainWindow.axaml"));
        var pathBoxes = document.Descendants(Presentation + "TextBox")
            .Where(element => ((string?)element.Attribute("Classes"))?.Contains(
                "pathBox", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(2, pathBoxes.Length);
        Assert.All(pathBoxes, textBox =>
            Assert.Equal("True", (string?)textBox.Attribute("IsReadOnly")));
        Assert.All(pathBoxes, textBox => Assert.Null((string?)textBox.Attribute("IsEnabled")));
        Assert.All(pathBoxes, textBox => Assert.Null((string?)textBox.Attribute("IsHitTestVisible")));
    }

    [Fact]
    public void ConfirmationDialogUsesSharedStylesAndDefaultsSafelyToCancel()
    {
        var dialog = XDocument.Load(ProductPath(
            "src", "ExtSieve.App", "Views", "ConfirmationDialog.axaml"));
        var cancel = dialog.Descendants(Presentation + "Button")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "CancelButton");
        var confirm = dialog.Descendants(Presentation + "Button")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "ConfirmButton");
        var application = XDocument.Load(ProductPath("src", "ExtSieve.App", "App.axaml"));
        var selectors = application.Descendants(Presentation + "Style")
            .Select(element => (string?)element.Attribute("Selector"))
            .Where(selector => selector is not null)
            .ToHashSet(StringComparer.Ordinal);
        var actionStyles = application.Descendants(Presentation + "Style")
            .Where(element => (string?)element.Attribute("Selector") is
                "Button.secondary" or "Button.primary")
            .ToDictionary(
                element => (string)element.Attribute("Selector")!,
                element => element.Elements(Presentation + "Setter")
                    .ToDictionary(
                        setter => (string)setter.Attribute("Property")!,
                        setter => (string?)setter.Attribute("Value")),
                StringComparer.Ordinal);

        Assert.Equal("secondary", (string?)cancel.Attribute("Classes"));
        Assert.Equal("True", (string?)cancel.Attribute("IsCancel"));
        Assert.Equal("True", (string?)cancel.Attribute("IsDefault"));
        Assert.Equal("primary", (string?)confirm.Attribute("Classes"));
        Assert.Null((string?)confirm.Attribute("IsDefault"));
        Assert.Contains("Button.secondary:pointerover", selectors);
        Assert.Contains("Button.secondary:pressed", selectors);
        Assert.Contains("Button.secondary:disabled", selectors);
        Assert.Contains("Button.primary:pointerover", selectors);
        Assert.Contains("Button.primary:pressed", selectors);
        Assert.Contains("Button.primary:disabled", selectors);
        Assert.Contains("Button:focus-visible", selectors);
        Assert.All(actionStyles.Values, setters =>
        {
            Assert.Equal("Center", setters["HorizontalContentAlignment"]);
            Assert.Equal("Center", setters["VerticalContentAlignment"]);
        });
    }

    [Fact]
    public void ExtensionRowsExposeLocalizedDisplayLabelsWithoutChangingCheckboxNames()
    {
        var document = XDocument.Load(ProductPath("src", "ExtSieve.App", "MainWindow.axaml"));
        var rowStyle = document.Descendants(Presentation + "Style")
            .Single(element => (string?)element.Attribute("Selector") == "ListBoxItem"
                && (string?)element.Attribute(Xaml + "DataType") == "vm:ExtensionGroupRowViewModel");
        var rowName = rowStyle.Elements(Presentation + "Setter")
            .Single(element => (string?)element.Attribute("Property") == "AutomationProperties.Name");
        var rowCheckBox = document.Descendants(Presentation + "CheckBox")
            .Single(element => (string?)element.Attribute("IsThreeState") is null);

        Assert.Equal("{Binding DisplayLabel}", (string?)rowName.Attribute("Value"));
        Assert.Equal(
            "{Binding DisplayLabel}",
            (string?)rowCheckBox.Attribute("AutomationProperties.Name"));
    }

    [Fact]
    public void ArchiveLayoutRetainsExclusiveRadioSemanticsAndRegistersArrowNavigation()
    {
        var document = XDocument.Load(ProductPath("src", "ExtSieve.App", "MainWindow.axaml"));
        var archiveButtons = document.Descendants(Presentation + "RadioButton")
            .Where(element => (string?)element.Attribute("GroupName") == "ArchiveMode")
            .ToArray();
        var codeBehind = File.ReadAllText(ProductPath(
            "src", "ExtSieve.App", "MainWindow.axaml.cs"));

        Assert.Equal(2, archiveButtons.Length);
        Assert.All(archiveButtons, element =>
        {
            Assert.NotNull((string?)element.Attribute("IsChecked"));
            Assert.NotNull((string?)element.Attribute("IsTabStop"));
            Assert.NotNull((string?)element.Attribute("AutomationProperties.Name"));
        });
        Assert.Contains("RoutingStrategies.Tunnel", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Key.Left or Key.Up", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Key.Right or Key.Down", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void TablerResourcesAndMitNoticeArePinnedAndComplete()
    {
        var document = XDocument.Load(ProductPath(
            "src", "ExtSieve.App", "Themes", "TablerIcons.axaml"));
        var keys = document.Root!.Elements()
            .Select(element => (string?)element.Attribute(Xaml + "Key"))
            .Where(key => key is not null)
            .ToHashSet(StringComparer.Ordinal);
        string[] requiredKeys =
        [
            "TablerRefresh",
            "TablerFolder",
            "TablerFile",
            "TablerFiles",
            "TablerStack",
            "TablerArchive",
            "TablerSettings",
            "TablerChevronUp",
            "TablerChevronDown",
            "TablerArrowLeft",
            "TablerX",
        ];

        Assert.All(requiredKeys, key => Assert.Contains(key, keys));

        var notice = File.ReadAllText(ProductPath("THIRD_PARTY_NOTICES.md"));
        Assert.Contains("Tabler Icons v3.46.0", notice, StringComparison.Ordinal);
        Assert.Contains("Permission is hereby granted", notice, StringComparison.Ordinal);
    }

    private static string ProductPath(params string[] parts) => Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
}
