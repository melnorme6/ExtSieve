using System.Text.Json;
using System.Windows.Input;
using ExtSieve.App.LocalData;
using ExtSieve.App.Localization;
using ExtSieve.App.Services;
using ExtSieve.Core.Models;

namespace ExtSieve.App.ViewModels;

public sealed record ProfileExtensionItem(string? Key, string Display, string Help, ICommand Command)
{
    public bool IsAdd => Key is null;
}

public sealed class ProfileManagerViewModel : LocalizedDialogViewModel
{
    private readonly IProfileStore _store;
    private readonly IFeatureDialogService _dialogs;
    private readonly Func<IReadOnlyList<string>> _selection;
    private ExtensionProfile? _selected;
    private bool _available;
    private bool _busy;
    private string? _warningKey;
    private string? _noticeKey;
    private object[] _noticeArguments = [];

    public ProfileManagerViewModel(LocalizationService localization, IProfileStore store,
        IFeatureDialogService dialogs, Func<IReadOnlyList<string>> selection) : base(localization)
    {
        _store = store;
        _dialogs = dialogs;
        _selection = selection;
        CreateCommand = new AsyncCommand(CreateAsync, () => CanWrite);
        ImportCommand = new AsyncCommand(ImportAsync, () => CanWrite);
        ExportCommand = new AsyncCommand(ExportAsync, () => !_busy && SelectedProfile is not null);
        RenameCommand = new AsyncCommand(RenameAsync, () => CanEditProfile);
        DuplicateCommand = new AsyncCommand(DuplicateAsync, () => CanEditProfile);
        DeleteCommand = new AsyncCommand(DeleteAsync, () => CanEditProfile);
        AddExtensionCommand = new AsyncCommand(AddExtensionAsync, () => CanEditProfile);
    }

    public AsyncCommand CreateCommand { get; }
    public AsyncCommand ImportCommand { get; }
    public AsyncCommand ExportCommand { get; }
    public AsyncCommand RenameCommand { get; }
    public AsyncCommand DuplicateCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public AsyncCommand AddExtensionCommand { get; }
    public IReadOnlyList<ExtensionProfile> Profiles { get; private set; } = [];
    public ExtensionProfile? SelectedProfile
    {
        get => _selected;
        set { _selected = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ExtensionItems)); RaisePropertyChanged(nameof(ExtensionSummary)); RaisePropertyChanged(nameof(HasSelectedProfile)); RaisePropertyChanged(nameof(CanEditProfile)); RefreshCommands(); }
    }
    public string Title => Localization["ManageProfiles"];
    public string CreateLabel => Localization["NewProfile"];
    public string ImportLabel => Localization["ImportProfiles"];
    public string ExportLabel => Localization["ExportProfile"];
    public string RenameLabel => Localization["RenameProfile"];
    public string DuplicateLabel => Localization["DuplicateProfile"];
    public string DeleteLabel => Localization["DeleteProfile"];
    public string AddExtensionLabel => Localization["AddProfileExtension"];
    public string EmptyLabel => Localization["ProfileSelectHelp"];
    public string Help => Localization["ProfilesHelp"];
    public string Warning => _warningKey is null ? string.Empty : Localization[_warningKey];
    public bool HasWarning => _warningKey is not null;
    public string Message => HasWarning ? Warning : _noticeKey is null ? "" : Format(_noticeKey, _noticeArguments);
    public bool HasMessage => Message.Length > 0;
    public bool CanWrite => _available && !_busy;
    public bool HasSelectedProfile => SelectedProfile is not null;
    public bool CanEditProfile => CanWrite && HasSelectedProfile;
    public string ExtensionSummary => Format(SelectedProfile?.ExtensionKeys.Count == 1
        ? "ProfileExtensionSingularFormat" : "ProfileExtensionPluralFormat", SelectedProfile?.ExtensionKeys.Count ?? 0);
    public IReadOnlyList<ProfileExtensionItem> ExtensionItems => (SelectedProfile?.ExtensionKeys ?? [])
        .Select(key => new ProfileExtensionItem(key, key.Length == 0 ? Localization["NoExtension"] : key,
            Format("RemoveProfileExtensionFormat", key.Length == 0 ? Localization["NoExtension"] : key),
            new AsyncCommand(() => RemoveExtensionAsync(key), () => CanEditProfile)))
        .Append(new ProfileExtensionItem(null, "", AddExtensionLabel, AddExtensionCommand)).ToArray();

    public async Task LoadAsync()
    {
        var result = await _store.LoadAsync().ConfigureAwait(true);
        _available = result.IsAvailable;
        Profiles = result.Value;
        _warningKey = result.IsAvailable ? null : "ProfilesLoadWarning";
        RaisePropertyChanged(string.Empty);
        RefreshCommands();
    }

    public string? ValidateName(string name, ExtensionProfile? except = null)
    {
        string normalized;
        try { normalized = ExtensionProfile.NormalizeName(name); }
        catch (ArgumentException) { return "ProfileInvalidName"; }
        return Profiles.Any(profile => !ReferenceEquals(profile, except)
            && string.Equals(profile.Name, normalized, StringComparison.OrdinalIgnoreCase)) ? "ProfileDuplicateName" : null;
    }

    public Task CreateAsync() => EditAsync(null, [], "NewProfile", allowEmpty: true);
    public Task SaveSelectionAsync() => EditAsync(null, _selection(), "SaveProfile", allowEmpty: false);
    public Task RenameAsync() => SelectedProfile is { } profile ? EditAsync(profile, profile.ExtensionKeys, "RenameProfile", true) : Task.CompletedTask;
    public Task DuplicateAsync() => SelectedProfile is { } profile
        ? EditAsync(null, profile.ExtensionKeys, "DuplicateProfile", true, SuggestDuplicateName(profile), profile.Name) : Task.CompletedTask;

    private string SuggestDuplicateName(ExtensionProfile profile)
    {
        for (var index = 1; index <= Profiles.Count + 1; index++)
        {
            var key = index == 1 ? "ProfileCopyNameFormat" : "ProfileCopyNumberedNameFormat";
            var suffixLength = Format(key, "", index).Length;
            var prefixLength = Math.Min(profile.Name.Length, 80 - suffixLength);
            if (prefixLength > 0 && char.IsHighSurrogate(profile.Name[prefixLength - 1])) prefixLength--;
            var candidate = Format(key, profile.Name[..prefixLength].TrimEnd(), index);
            if (ValidateName(candidate) is null) return candidate;
        }
        throw new InvalidOperationException("No available profile copy name.");
    }

    private async Task EditAsync(ExtensionProfile? replaced, IReadOnlyList<string> keys, string titleKey, bool allowEmpty,
        string? initialName = null, string? duplicateSourceName = null)
    {
        if (!CanWrite || (!allowEmpty && keys.Count == 0)) return;
        SetBusy(true);
        try
        {
            using var input = new ProfileNameViewModel(Localization, titleKey, keys, name => ValidateName(name, replaced),
                initialName ?? replaced?.Name ?? "", allowEmpty, duplicateSourceName);
            var name = await _dialogs.RequestProfileNameAsync(input).ConfigureAwait(true);
            if (name is null || ValidateName(name, replaced) is not null) return;
            var profile = ExtensionProfile.Create(name, keys);
            await CommitAsync(Profiles.Where(item => !ReferenceEquals(item, replaced)).Append(profile).ToArray(), profile).ConfigureAwait(true);
        }
        finally { SetBusy(false); }
    }

    public async Task DeleteAsync()
    {
        if (!CanEditProfile || SelectedProfile is not { } profile) return;
        SetBusy(true);
        try
        {
            if (await _dialogs.ConfirmDeleteProfileAsync(DeleteLabel, Format("DeleteProfileMessageFormat", profile.Name), DeleteLabel, CancelLabel))
            {
                var remaining = Profiles.Where(item => !ReferenceEquals(item, profile)).ToArray();
                var index = Profiles.ToList().IndexOf(profile);
                await CommitAsync(remaining, remaining.Length == 0 ? null : remaining[Math.Min(index, remaining.Length - 1)]).ConfigureAwait(true);
            }
        }
        finally { SetBusy(false); }
    }

    public async Task AddExtensionAsync()
    {
        if (!CanEditProfile || SelectedProfile is not { } profile) return;
        SetBusy(true);
        try
        {
            using var input = new ProfileExtensionViewModel(Localization, profile.ExtensionKeys);
            var key = await _dialogs.RequestProfileExtensionAsync(input).ConfigureAwait(true);
            if (key is null) return;
            try { key = ExtensionProfile.NormalizeExtensionKey(key); }
            catch (ArgumentException) { SetWarning("ProfileExtensionInvalid"); return; }
            if (profile.ExtensionKeys.Contains(key, StringComparer.Ordinal)) { SetWarning("ProfileExtensionDuplicate"); return; }
            await ReplaceKeysAsync(profile, profile.ExtensionKeys.Append(key)).ConfigureAwait(true);
        }
        finally { SetBusy(false); }
    }

    public async Task RemoveExtensionAsync(string key)
    {
        if (!CanEditProfile || SelectedProfile is not { } profile || !profile.ExtensionKeys.Contains(key, StringComparer.Ordinal)) return;
        SetBusy(true);
        try { await ReplaceKeysAsync(profile, profile.ExtensionKeys.Where(item => item != key)).ConfigureAwait(true); }
        finally { SetBusy(false); }
    }

    private Task<bool> ReplaceKeysAsync(ExtensionProfile profile, IEnumerable<string> keys)
    {
        var edited = ExtensionProfile.Create(profile.Name, keys);
        return CommitAsync(Profiles.Select(item => ReferenceEquals(item, profile) ? edited : item).ToArray(), edited);
    }

    public async Task ImportAsync()
    {
        if (!CanWrite) return;
        SetBusy(true);
        try
        {
            var imported = await _dialogs.ImportProfilesAsync(ImportLabel, Localization["ProfileJsonFiles"]).ConfigureAwait(true);
            if (imported is null) return;
            var validated = JsonProfileStore.Normalize(imported);
            var added = validated.Where(profile => !Profiles.Any(existing => string.Equals(existing.Name, profile.Name, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (added.Length > 0 && !await CommitAsync(Profiles.Concat(added).ToArray(), added[0]).ConfigureAwait(true)) return;
            _warningKey = null;
            _noticeKey = "ProfileImportSummaryFormat";
            _noticeArguments = [added.Length, validated.Count - added.Length];
            RaiseMessageChanged();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException) { SetWarning("ProfileImportWarning"); }
        finally { SetBusy(false); }
    }

    public async Task ExportAsync()
    {
        if (_busy || SelectedProfile is not { } profile) return;
        SetBusy(true);
        try
        {
            if (await _dialogs.ExportProfileAsync(profile, ExportLabel, Localization["ProfileJsonFiles"]).ConfigureAwait(true))
            {
                _warningKey = null;
                _noticeKey = "ProfileExportSuccess";
                _noticeArguments = [];
                RaiseMessageChanged();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { SetWarning("ProfileExportWarning"); }
        finally { SetBusy(false); }
    }

    private async Task<bool> CommitAsync(IReadOnlyList<ExtensionProfile> profiles, ExtensionProfile? selected)
    {
        try
        {
            var sorted = profiles.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            await _store.SaveAsync(sorted).ConfigureAwait(true);
            Profiles = sorted;
            RaisePropertyChanged(nameof(Profiles));
            SelectedProfile = selected;
            _warningKey = null;
            _noticeKey = null;
            RaiseMessageChanged();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { SetWarning("ProfilesSaveWarning"); return false; }
    }

    private void SetWarning(string key) { _warningKey = key; RaiseMessageChanged(); }
    private void RaiseMessageChanged()
    {
        RaisePropertyChanged(nameof(Warning)); RaisePropertyChanged(nameof(HasWarning));
        RaisePropertyChanged(nameof(Message)); RaisePropertyChanged(nameof(HasMessage));
    }
    private void SetBusy(bool value) { _busy = value; RaisePropertyChanged(nameof(CanWrite)); RaisePropertyChanged(nameof(CanEditProfile)); RefreshCommands(); }
    public void RefreshCommands()
    {
        CreateCommand.RaiseCanExecuteChanged(); ImportCommand.RaiseCanExecuteChanged(); ExportCommand.RaiseCanExecuteChanged();
        RenameCommand.RaiseCanExecuteChanged(); DuplicateCommand.RaiseCanExecuteChanged(); DeleteCommand.RaiseCanExecuteChanged(); AddExtensionCommand.RaiseCanExecuteChanged();
    }
}
