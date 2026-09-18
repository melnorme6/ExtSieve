using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ExtSieve.App.ViewModels;

namespace ExtSieve.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ArchiveModeGroup.AddHandler(
            InputElement.KeyDownEvent,
            OnArchiveModeKeyDown,
            RoutingStrategies.Tunnel);
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            await viewModel.InitializeAsync();
            if (!viewModel.HasSource)
            {
                ChooseFolderButton.Focus();
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape
            && DataContext is MainWindowViewModel { IsSettingsViewOpen: true } viewModel)
        {
            viewModel.CloseSettings();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnClosed(e);
    }

    private void OnArchiveModeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not RadioButton current
            || (!ReferenceEquals(current, PreserveStructureRadioButton)
                && !ReferenceEquals(current, FlattenRadioButton)))
        {
            return;
        }

        var movePrevious = e.Key is Key.Left or Key.Up;
        var moveNext = e.Key is Key.Right or Key.Down;
        if (!movePrevious && !moveNext)
        {
            return;
        }

        e.Handled = true;
        RadioButton? target = (current, movePrevious, moveNext) switch
        {
            (_, true, _) when ReferenceEquals(current, FlattenRadioButton)
                => PreserveStructureRadioButton,
            (_, _, true) when ReferenceEquals(current, PreserveStructureRadioButton)
                => FlattenRadioButton,
            _ => null,
        };

        if (target is null)
        {
            return;
        }

        target.IsChecked = true;
        target.Focus();
    }

    private void OnClearFilterClick(object? sender, RoutedEventArgs e)
    {
        FilterTextBox.Focus();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(MainWindowViewModel.IsSettingsViewOpen)
            || sender is not MainWindowViewModel viewModel) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (viewModel.IsSettingsViewOpen)
            {
                SettingsPage.FocusBackButton();
            }
            else
            {
                SettingsButton.Focus();
            }
        });
    }
}
