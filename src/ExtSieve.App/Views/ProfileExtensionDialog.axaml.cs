using Avalonia.Controls;
using Avalonia.Interactivity;
using ExtSieve.App.ViewModels;

namespace ExtSieve.App.Views;

public partial class ProfileExtensionDialog : Window
{
    public ProfileExtensionDialog() { InitializeComponent(); Opened += (_, _) => ExtensionBox.Focus(); }
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProfileExtensionViewModel { CanAdd: true } viewModel) Close(viewModel.NormalizedKey);
    }
}
