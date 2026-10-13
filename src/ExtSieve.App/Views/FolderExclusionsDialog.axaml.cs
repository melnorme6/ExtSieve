using Avalonia.Controls;
using Avalonia.Interactivity;
using ExtSieve.App.ViewModels;

namespace ExtSieve.App.Views;

public partial class FolderExclusionsDialog : Window
{
    public FolderExclusionsDialog() { InitializeComponent(); Opened += (_, _) => SearchBox.Focus(); }
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
    private void OnApply(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FolderExclusionsViewModel { CanApply: true }) Close(true);
    }
}
