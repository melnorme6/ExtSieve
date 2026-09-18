using Avalonia.Controls;
using ExtSieve.App.Views;

namespace ExtSieve.App.Services;

public sealed class WindowPromptService(Window owner) : IUserPromptService
{
    private readonly Window _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmLabel,
        string cancelLabel)
    {
        var dialog = new ConfirmationDialog(
            title,
            message,
            confirmLabel,
            cancelLabel);
        return await dialog.ShowDialog<bool>(_owner);
    }
}
