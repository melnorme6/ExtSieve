namespace ExtSieve.Core.Utilities;

public static class PathRules
{
    public static StringComparison PlatformComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static StringComparer PlatformComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static bool IsWithinRoot(string candidatePath, string rootPath)
    {
        var candidate = Path.GetFullPath(candidatePath);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (string.Equals(candidate, root, PlatformComparison))
        {
            return true;
        }

        var rootPrefix = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootPrefix, PlatformComparison);
    }

    public static string NormalizeRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return relativePath.Replace('\\', '/');
    }

    public static string NormalizeAndValidateArchiveEntryPath(string entryPath)
    {
        var normalizedPath = NormalizeRelativePath(entryPath);
        if (normalizedPath.StartsWith('/')
            || (normalizedPath.Length >= 2
                && char.IsAsciiLetter(normalizedPath[0])
                && normalizedPath[1] == ':'))
        {
            throw new InvalidDataException("Archive entry paths must be relative.");
        }

        var segments = normalizedPath.Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new InvalidDataException(
                "Archive entry paths cannot contain empty or traversal segments.");
        }

        return normalizedPath;
    }
}
