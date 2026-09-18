using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ExtSieve.App.Services;

public sealed class AvaloniaPathPickerService(Window owner) : IPathPickerService
{
    private readonly Window _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public async Task<string?> PickSourceFolderAsync(string title)
    {
        var folders = await _owner.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
            });

        return folders.Count == 1 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickArchiveDestinationAsync(
        string title,
        string suggestedFileName)
    {
        var file = await _owner.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedFileName,
                DefaultExtension = "zip",
                FileTypeChoices =
                [
                    new FilePickerFileType("ZIP")
                    {
                        Patterns = ["*.zip"],
                    },
                ],
            });

        return file?.TryGetLocalPath();
    }

    public Task OpenFolderAsync(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.GetFullPath(folderPath),
            UseShellExecute = true,
        });
        return Task.CompletedTask;
    }
}
