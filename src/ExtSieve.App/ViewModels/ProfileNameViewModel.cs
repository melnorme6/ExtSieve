using ExtSieve.App.Localization;

namespace ExtSieve.App.ViewModels;

public sealed class ProfileNameViewModel(
    LocalizationService localization,
    string titleKey,
    IReadOnlyList<string> keys,
    Func<string, string?> validate,
    string initialName = "",
    bool allowEmpty = false,
    string? duplicateSourceName = null) : LocalizedDialogViewModel(localization)
{
    private string _name = initialName;
    public string Title => Localization[titleKey];
    public string NameLabel => Localization[titleKey == "DuplicateProfile" ? "NewProfileName" : "ProfileName"];
    public string Help => titleKey switch
    {
        "NewProfile" => Localization["ProfileEmptyHelp"],
        "RenameProfile" => "",
        "DuplicateProfile" => Format("ProfileDuplicateHelpFormat", duplicateSourceName ?? ""),
        _ => Localization["ProfileSaveHelp"]
    };
    public bool HasHelp => Help.Length > 0;
    public bool ShowExtensions => titleKey == "SaveProfile";
    public bool SelectInitialName => duplicateSourceName is not null;
    public bool IsCompact => !ShowExtensions;
    public bool HasError => Error.Length > 0;
    public string SaveLabel => Localization["Save"];
    public string ExtensionSummary => Format("ProfileExtensionsFormat", keys.Count);
    public string ExtensionDisplay => string.Join(", ", keys.Select(key => key.Length == 0 ? Localization["NoExtension"] : key));
    public string Name
    {
        get => _name;
        set { _name = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(Error)); RaisePropertyChanged(nameof(HasError)); RaisePropertyChanged(nameof(CanSave)); }
    }
    public string Error => validate(_name) is { } key ? Localization[key] : string.Empty;
    public bool CanSave => validate(_name) is null && (allowEmpty || keys.Count > 0);
}
