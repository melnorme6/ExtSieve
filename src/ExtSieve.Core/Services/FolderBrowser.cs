using ExtSieve.Core.Models;
using ExtSieve.Core.Utilities;

namespace ExtSieve.Core.Services;

public sealed class FolderBrowser : IFolderBrowser
{
    public Task<FolderChildren> GetChildrenAsync(string root, string relativeDirectory, CancellationToken cancellationToken) =>
        Task.Run(() => GetChildren(root, relativeDirectory, cancellationToken), cancellationToken);

    private static FolderChildren GetChildren(string root, string relativeDirectory, CancellationToken cancellationToken)
    {
        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var relative = relativeDirectory.Length == 0 ? "" : FolderExclusionSet.NormalizePath(relativeDirectory);
        var directory = Path.Combine(canonicalRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        var paths = new List<string>();
        var warning = false;
        try
        {
            // Check every ancestor as well as the target before directory enumeration.
            var current = canonicalRoot;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return new FolderChildren([], true);
            foreach (var segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (!PathRules.IsWithinRoot(current, canonicalRoot)
                    || (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return new FolderChildren([], true);
            }
            foreach (var path in Directory.EnumerateDirectories(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (PathRules.IsWithinRoot(path, canonicalRoot)
                        && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                        paths.Add(FolderExclusionSet.NormalizePath(Path.GetRelativePath(canonicalRoot, path)));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { warning = true; }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { warning = true; }
        paths.Sort(StringComparer.Ordinal);
        return new FolderChildren(paths.AsReadOnly(), warning);
    }
}
