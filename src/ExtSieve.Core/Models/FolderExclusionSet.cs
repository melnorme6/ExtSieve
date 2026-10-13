using ExtSieve.Core.Utilities;

namespace ExtSieve.Core.Models;

public sealed class FolderExclusionSet
{
    private readonly HashSet<string> _paths;

    public FolderExclusionSet(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var normalized = paths.Select(NormalizePath).Distinct(PathRules.PlatformComparer)
            .OrderBy(path => path.Count(character => character == '/'))
            .ThenBy(path => path, StringComparer.Ordinal);
        _paths = new HashSet<string>(PathRules.PlatformComparer);
        foreach (var path in normalized)
            if (!Contains(path)) _paths.Add(path);
        Paths = Array.AsReadOnly(_paths.Order(StringComparer.Ordinal).ToArray());
    }

    public IReadOnlyList<string> Paths { get; }

    public bool Contains(string relativePath)
    {
        var path = NormalizePath(relativePath);
        if (_paths.Contains(path)) return true;
        for (var index = path.LastIndexOf('/'); index >= 0; index = path.LastIndexOf('/', index - 1))
        {
            if (_paths.Contains(path[..index])) return true;
            if (index == 0) break;
        }
        return false;
    }

    public static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Contains('*')
            || normalized.Contains('?') || normalized.Any(char.IsControl)
            || normalized.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
            throw new ArgumentException("Exclusions must be exact relative directory paths without traversal or patterns.", nameof(path));
        return normalized;
    }
}
