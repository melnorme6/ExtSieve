using Avalonia.Controls;
using Avalonia.Interactivity;
using ExtSieve.App.ViewModels;

namespace ExtSieve.App.Views;

public partial class ProfileNameDialog : Window
{
    public ProfileNameDialog()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            NameBox.Focus();
            if (DataContext is ProfileNameViewModel { SelectInitialName: true }) NameBox.SelectAll();
        };
    }
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProfileNameViewModel { CanSave: true } viewModel) Close(viewModel.Name);
    }
}
