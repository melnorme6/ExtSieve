using System.Diagnostics;
using ExtSieve.Core.Models;
using ExtSieve.Core.Utilities;

namespace ExtSieve.Core.Services;

public sealed class FolderScanner : IFolderScanner
{
    public Task<ScanResult> ScanAsync(
        string sourceDirectory,
        IProgress<long>? discoveredFileProgress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        return Task.Run(
            () => Scan(sourceDirectory, discoveredFileProgress, cancellationToken),
            cancellationToken);
    }

    private static ScanResult Scan(
        string sourceDirectory,
        IProgress<long>? discoveredFileProgress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDirectory));
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Source directory does not exist: {root}");
        }

        var rootAttributes = File.GetAttributes(root);
        if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The selected source directory cannot be a link or reparse point.");
        }

        var files = new List<ScannedFile>();
        var warnings = new List<ScanWarning>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);
        long discoveredFileCount = 0;

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pendingDirectories.Pop();
            EnumerateDirectory(
                directory,
                root,
                pendingDirectories,
                files,
                warnings,
                ref discoveredFileCount,
                discoveredFileProgress,
                cancellationToken);
        }

        files.Sort(ScannedFileRelativePathComparer.Instance);
        warnings.Sort(ScanWarningComparer.Instance);
        var groups = files
            .GroupBy(file => file.ExtensionKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ExtensionGroup(
                group.Key,
                group.LongCount(),
                group.Sum(file => file.Size)))
            .OrderBy(group => group.ExtensionKey, StringComparer.Ordinal)
            .ToArray();

        stopwatch.Stop();
        return new ScanResult(files.ToArray(), groups, warnings.ToArray(), stopwatch.Elapsed);
    }

    private static void EnumerateDirectory(
        string directory,
        string root,
        Stack<string> pendingDirectories,
        List<ScannedFile> files,
        List<ScanWarning> warnings,
        ref long discoveredFileCount,
        IProgress<long>? discoveredFileProgress,
        CancellationToken cancellationToken)
    {
        IEnumerator<string>? enumerator = null;
        try
        {
            enumerator = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
            while (MoveNext(enumerator, directory, warnings))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = enumerator.Current;
                ProcessEntry(
                    path,
                    root,
                    pendingDirectories,
                    files,
                    warnings,
                    ref discoveredFileCount,
                    discoveredFileProgress);
            }
        }
        catch (Exception exception) when (IsRecoverableFileSystemException(exception))
        {
            warnings.Add(new ScanWarning(ScanWarningKind.InaccessibleDirectory, directory));
        }
        finally
        {
            enumerator?.Dispose();
        }
    }

    private static bool MoveNext(
        IEnumerator<string> enumerator,
        string directory,
        List<ScanWarning> warnings)
    {
        try
        {
            return enumerator.MoveNext();
        }
        catch (Exception exception) when (IsRecoverableFileSystemException(exception))
        {
            warnings.Add(new ScanWarning(ScanWarningKind.InaccessibleDirectory, directory));
            return false;
        }
    }

    private static void ProcessEntry(
        string path,
        string root,
        Stack<string> pendingDirectories,
        List<ScannedFile> files,
        List<ScanWarning> warnings,
        ref long discoveredFileCount,
        IProgress<long>? discoveredFileProgress)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!PathRules.IsWithinRoot(fullPath, root))
            {
                warnings.Add(new ScanWarning(ScanWarningKind.LinkSkipped, fullPath));
                return;
            }

            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                warnings.Add(new ScanWarning(ScanWarningKind.LinkSkipped, fullPath));
                return;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                pendingDirectories.Push(fullPath);
                return;
            }

            var file = new FileInfo(fullPath);
            var relativePath = PathRules.NormalizeRelativePath(Path.GetRelativePath(root, fullPath));
            files.Add(new ScannedFile(
                fullPath,
                relativePath,
                ExtensionClassifier.GetGroupKey(file.Name),
                file.Length,
                file.LastWriteTimeUtc));
            discoveredFileCount++;
            discoveredFileProgress?.Report(discoveredFileCount);
        }
        catch (Exception exception) when (IsRecoverableFileSystemException(exception))
        {
            warnings.Add(new ScanWarning(ScanWarningKind.UnreadableFile, Path.GetFullPath(path)));
        }
    }

    private static bool IsRecoverableFileSystemException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException;
    }

    private sealed class ScannedFileRelativePathComparer : IComparer<ScannedFile>
    {
        public static ScannedFileRelativePathComparer Instance { get; } = new();

        public int Compare(ScannedFile? left, ScannedFile? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            var insensitive = StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath);
            return insensitive != 0
                ? insensitive
                : StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath);
        }
    }

    private sealed class ScanWarningComparer : IComparer<ScanWarning>
    {
        public static ScanWarningComparer Instance { get; } = new();

        public int Compare(ScanWarning? left, ScanWarning? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            var byPath = StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path);
            return byPath != 0 ? byPath : left.Kind.CompareTo(right.Kind);
        }
    }
}
