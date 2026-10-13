using ExtSieve.Core.Models;

namespace ExtSieve.App.LocalData;

public sealed class JsonProfileStore(string path) : IProfileStore
{
    private readonly AtomicJsonFile<ProfileDocument> _file = new(path);

    public async Task<LocalDataResult<IReadOnlyList<ExtensionProfile>>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var result = await _file.LoadAsync(new ProfileDocument(1, []), cancellationToken).ConfigureAwait(false);
        if (!result.IsAvailable || result.Value.SchemaVersion != 1 || result.Value.Profiles is null) return new([], false);
        try { return new(Normalize(result.Value.Profiles), true); }
        catch (ArgumentException) { return new([], false); }
    }

    public async Task SaveAsync(IReadOnlyList<ExtensionProfile> profiles, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(profiles);
        // Never replace unreadable, corrupt, or newer-schema data with an empty fallback.
        if (!(await LoadAsync(cancellationToken).ConfigureAwait(false)).IsAvailable)
            throw new IOException("Profile storage is unavailable or invalid.");
        await _file.SaveAsync(new ProfileDocument(1, normalized), cancellationToken).ConfigureAwait(false);
    }

    internal static System.Collections.ObjectModel.ReadOnlyCollection<ExtensionProfile> Normalize(IReadOnlyList<ExtensionProfile> profiles)
    {
        var normalized = profiles.Select(profile => profile is null || profile.ExtensionKeys is null
            ? throw new ArgumentException("Invalid profile.")
            : ExtensionProfile.Create(profile.Name, profile.ExtensionKeys)).OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (normalized.Select(profile => profile.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
            throw new ArgumentException("Profile names must be unique.", nameof(profiles));
        return Array.AsReadOnly(normalized);
    }

    private sealed record ProfileDocument(int SchemaVersion, IReadOnlyList<ExtensionProfile> Profiles);
}
