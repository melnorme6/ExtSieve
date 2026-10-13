using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ExtSieve.App.Views;

public partial class ProfilesDialog : Window
{
    public ProfilesDialog() { InitializeComponent(); Opened += (_, _) => ProfileList.Focus(); }
    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
