namespace ExtSieve.App.Services;

public interface IPathPickerService
{
    Task<string?> PickSourceFolderAsync(string title);

    Task<string?> PickArchiveDestinationAsync(string title, string suggestedFileName);

    Task OpenFolderAsync(string folderPath);
}
