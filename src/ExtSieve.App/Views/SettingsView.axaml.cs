using Avalonia.Controls;

namespace ExtSieve.App.Views;

public partial class SettingsView : UserControl
{
    private const double NarrowLayoutThreshold = 720;

    public SettingsView()
    {
        InitializeComponent();
        SizeChanged += (_, eventArgs) => UpdateCardLayout(eventArgs.NewSize.Width);
    }

    public void FocusBackButton() => BackButton.Focus();

    private void UpdateCardLayout(double width)
    {
        var stacksCards = width < NarrowLayoutThreshold;
        SettingsCards.ColumnDefinitions = new ColumnDefinitions(stacksCards ? "*" : "*,*");
        Grid.SetColumn(LanguageCard, stacksCards ? 0 : 1);
        Grid.SetRow(LanguageCard, stacksCards ? 1 : 0);
    }
}
