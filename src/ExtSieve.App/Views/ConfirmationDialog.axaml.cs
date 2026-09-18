using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ExtSieve.App.Views;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog()
    {
        InitializeComponent();
    }

    public ConfirmationDialog(
        string title,
        string message,
        string confirmLabel,
        string cancelLabel)
        : this()
    {
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;
        CancelButton.Content = cancelLabel;
        AutomationProperties.SetName(ConfirmButton, confirmLabel);
        AutomationProperties.SetName(CancelButton, cancelLabel);
        Opened += (_, _) => CancelButton.Focus();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
