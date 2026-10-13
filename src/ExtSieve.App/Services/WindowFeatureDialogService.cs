using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ExtSieve.App.LocalData;
using ExtSieve.App.ViewModels;
using ExtSieve.App.Views;
using ExtSieve.Core.Models;

namespace ExtSieve.App.Services;

public sealed class WindowFeatureDialogService(Window owner) : IFeatureDialogService
{
    private Window _owner = owner;

    public Task<string?> RequestProfileNameAsync(ProfileNameViewModel viewModel) =>
        new ProfileNameDialog { DataContext = viewModel }.ShowDialog<string?>(_owner);

    public Task<string?> RequestProfileExtensionAsync(ProfileExtensionViewModel viewModel) =>
        new ProfileExtensionDialog { DataContext = viewModel }.ShowDialog<string?>(_owner);

    public async Task<IReadOnlyList<ExtensionProfile>?> ImportProfilesAsync(string title, string fileTypeLabel)
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(fileTypeLabel) { Patterns = ["*.json"], MimeTypes = ["application/json"] }]
        });
        if (files.Count == 0) return null;
        using var file = files[0];
        await using var stream = await file.OpenReadAsync().ConfigureAwait(true);
        return await ProfileExchangeJson.ReadAsync(stream).ConfigureAwait(true);
    }

    public async Task<bool> ExportProfileAsync(ExtensionProfile profile, string title, string fileTypeLabel)
    {
        using var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = "ExtSieve-profile.json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType(fileTypeLabel) { Patterns = ["*.json"], MimeTypes = ["application/json"] }]
        });
        if (file is null) return false;
        var path = file.TryGetLocalPath() ?? throw new IOException("A local profile destination is required.");
        await ProfileExchangeJson.WriteAsync(path, profile).ConfigureAwait(true);
        return true;
    }

    public async Task ManageProfilesAsync(ProfileManagerViewModel viewModel)
    {
        var previous = _owner;
        var dialog = new ProfilesDialog { DataContext = viewModel };
        _owner = dialog;
        try { await dialog.ShowDialog(previous); }
        finally { _owner = previous; }
    }

    public Task<bool> EditExclusionsAsync(FolderExclusionsViewModel viewModel) =>
        new FolderExclusionsDialog { DataContext = viewModel }.ShowDialog<bool>(_owner);

    public Task<bool> ConfirmDeleteProfileAsync(string title, string message, string deleteLabel, string cancelLabel) =>
        new ConfirmationDialog(title, message, deleteLabel, cancelLabel).ShowDialog<bool>(_owner);
}
