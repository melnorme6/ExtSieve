using System.Windows.Input;

namespace ExtSieve.App.ViewModels;

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    private readonly Action _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    private readonly Func<bool>? _canExecute = canExecute;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand<T>(Action<T> execute, Func<T, bool>? canExecute = null) : ICommand where T : class
{
    private readonly Action<T> _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => parameter is T value && (canExecute?.Invoke(value) ?? true);
    public void Execute(object? parameter)
    {
        if (parameter is T value && CanExecute(value)) _execute(value);
    }
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
