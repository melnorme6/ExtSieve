using System.Security.Cryptography;
using System.Text;
using ExtSieve.Core.Models;

namespace ExtSieve.App.LocalData;

public sealed class JsonExclusionStore(string path) : IExclusionStore
{
    private readonly AtomicJsonFile<ExclusionDocument> _file = new(path);
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    public void Dispose() => _saveLock.Dispose();

    public static string GetRootKey(string sourceRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
        var identity = OperatingSystem.IsWindows() ? "windows:" + root.ToUpperInvariant() : "unix:" + root;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    public async Task<LocalDataResult<FolderExclusionSet>> LoadAsync(string sourceRoot, CancellationToken cancellationToken = default)
    {
        var result = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsAvailable) return new(new FolderExclusionSet([]), false);
        return new(new FolderExclusionSet(result.Value.Roots.GetValueOrDefault(GetRootKey(sourceRoot)) ?? []), true);
    }

    public async Task SaveAsync(string sourceRoot, FolderExclusionSet exclusions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exclusions);
        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
            if (!result.IsAvailable) throw new IOException("Exclusion storage is unavailable or invalid.");
            var roots = new SortedDictionary<string, IReadOnlyList<string>>(result.Value.Roots, StringComparer.Ordinal);
            var key = GetRootKey(sourceRoot);
            if (exclusions.Paths.Count == 0) roots.Remove(key);
            else roots[key] = exclusions.Paths;
            await _file.SaveAsync(new ExclusionDocument(1, roots), cancellationToken).ConfigureAwait(false);
        }
        finally { _saveLock.Release(); }
    }

    private async Task<LocalDataResult<ExclusionDocument>> LoadDocumentAsync(CancellationToken cancellationToken)
    {
        var empty = new ExclusionDocument(1, new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));
        var result = await _file.LoadAsync(empty, cancellationToken).ConfigureAwait(false);
        if (!result.IsAvailable || result.Value.SchemaVersion != 1 || result.Value.Roots is null) return new(empty, false);
        try
        {
            foreach (var (key, paths) in result.Value.Roots)
            {
                if (key.Length != 64 || key.Any(character => !char.IsAsciiHexDigit(character)) || paths is null)
                    return new(empty, false);
                _ = new FolderExclusionSet(paths);
            }
            return result;
        }
        catch (ArgumentException) { return new(empty, false); }
    }

    private sealed record ExclusionDocument(int SchemaVersion, SortedDictionary<string, IReadOnlyList<string>> Roots);
}
