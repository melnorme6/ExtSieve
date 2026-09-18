namespace ExtSieve.App.Services;

public interface IUserPromptService
{
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmLabel,
        string cancelLabel);
}
