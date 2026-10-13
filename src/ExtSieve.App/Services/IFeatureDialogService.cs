using ExtSieve.App.ViewModels;
using ExtSieve.Core.Models;

namespace ExtSieve.App.Services;

public interface IFeatureDialogService
{
    Task<string?> RequestProfileNameAsync(ProfileNameViewModel viewModel);
    Task<string?> RequestProfileExtensionAsync(ProfileExtensionViewModel viewModel);
    Task<IReadOnlyList<ExtensionProfile>?> ImportProfilesAsync(string title, string fileTypeLabel);
    Task<bool> ExportProfileAsync(ExtensionProfile profile, string title, string fileTypeLabel);
    Task ManageProfilesAsync(ProfileManagerViewModel viewModel);
    Task<bool> EditExclusionsAsync(FolderExclusionsViewModel viewModel);
    Task<bool> ConfirmDeleteProfileAsync(string title, string message, string deleteLabel, string cancelLabel);
}
