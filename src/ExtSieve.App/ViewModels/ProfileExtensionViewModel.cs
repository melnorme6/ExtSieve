using ExtSieve.App.Localization;
using ExtSieve.Core.Models;

namespace ExtSieve.App.ViewModels;

public sealed class ProfileExtensionViewModel(LocalizationService localization, IReadOnlyList<string> existingKeys)
    : LocalizedDialogViewModel(localization)
{
    private string _input = "";
    private bool _extensionless;
    public string Title => Localization["AddProfileExtension"];
    public string Help => Localization["ProfileExtensionHelp"];
    public string ExtensionLabel => Localization["Extension"];
    public string ExtensionlessLabel => Localization["NoExtension"];
    public string AddLabel => Localization["AddProfileExtension"];
    public string Input { get => _input; set { _input = value; UpdateValidation(); } }
    public bool Extensionless { get => _extensionless; set { _extensionless = value; UpdateValidation(); } }
    public bool CanEnterKey => !_extensionless;
    public string? NormalizedKey
    {
        get
        {
            if (_extensionless) return "";
            try { return ExtensionProfile.NormalizeExtensionInput(_input); }
            catch (ArgumentException) { return null; }
        }
    }
    public string Error => NormalizedKey is not { } key ? Localization["ProfileExtensionInvalid"]
        : existingKeys.Contains(key, StringComparer.Ordinal) ? Localization["ProfileExtensionDuplicate"] : "";
    public bool CanAdd => Error.Length == 0;
    private void UpdateValidation()
    {
        RaisePropertyChanged(string.Empty);
    }
}
