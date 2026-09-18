using ExtSieve.Core.Models;
using ExtSieve.Core.Utilities;

namespace ExtSieve.Core.Services;

public sealed class ArchivePlanner : IArchivePlanner
{
    public ArchivePlan CreatePlan(ArchiveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationPath);
        ArgumentNullException.ThrowIfNull(request.SelectedExtensionKeys);
        ArgumentNullException.ThrowIfNull(request.Manifest);

        var sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.SourceDirectory));
        var destination = Path.GetFullPath(request.DestinationPath);
        var selectedKeys = new HashSet<string>(
            request.SelectedExtensionKeys,
            StringComparer.OrdinalIgnoreCase);

        var selectedFiles = request.Manifest
            .Where(file => selectedKeys.Contains(file.ExtensionKey))
            .Select(file => ValidateAndNormalize(file, sourceRoot))
            .Where(file => !PathRules.PlatformComparer.Equals(file.AbsolutePath, destination))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();

        var entries = request.Mode switch
        {
            ArchiveMode.PreserveStructure => CreatePreservedEntries(selectedFiles),
            ArchiveMode.Flatten => CreateFlattenedEntries(selectedFiles),
            _ => throw new ArgumentOutOfRangeException(nameof(request), "Unsupported archive mode."),
        };

        return new ArchivePlan(
            entries,
            entries.Count(entry => entry.WasRenamed),
            entries.Sum(entry => entry.Source.Size));
    }

    private static ScannedFile ValidateAndNormalize(ScannedFile file, string sourceRoot)
    {
        ArgumentNullException.ThrowIfNull(file);
        var absolutePath = Path.GetFullPath(file.AbsolutePath);
        if (!PathRules.IsWithinRoot(absolutePath, sourceRoot))
        {
            throw new InvalidDataException("A scanned file is outside the selected source directory.");
        }

        var suppliedRelativePath = PathRules.NormalizeAndValidateArchiveEntryPath(
            file.RelativePath);

        var actualRelativePath = PathRules.NormalizeAndValidateArchiveEntryPath(
            Path.GetRelativePath(sourceRoot, absolutePath));
        if (!string.Equals(
                suppliedRelativePath,
                actualRelativePath,
                PathRules.PlatformComparison))
        {
            throw new InvalidDataException(
                "A scanned file's relative path does not match its absolute path.");
        }

        return file with { AbsolutePath = absolutePath, RelativePath = actualRelativePath };
    }

    private static ArchiveEntryPlan[] CreatePreservedEntries(ScannedFile[] files)
    {
        return files
            .Select(file => new ArchiveEntryPlan(file, file.RelativePath, WasRenamed: false))
            .ToArray();
    }

    private static ArchiveEntryPlan[] CreateFlattenedEntries(ScannedFile[] files)
    {
        var remainingOriginalNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var fileName = GetSafeFileName(file.RelativePath);
            remainingOriginalNames[fileName] = remainingOriginalNames.GetValueOrDefault(fileName) + 1;
        }

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<ArchiveEntryPlan>(files.Length);
        foreach (var file in files)
        {
            var originalName = GetSafeFileName(file.RelativePath);
            RemoveOne(remainingOriginalNames, originalName);

            var entryName = originalName;
            var wasRenamed = false;
            if (!usedNames.Add(entryName))
            {
                entryName = FindAvailableName(originalName, usedNames, remainingOriginalNames);
                usedNames.Add(entryName);
                wasRenamed = true;
            }

            entries.Add(new ArchiveEntryPlan(file, entryName, wasRenamed));
        }

        return entries.ToArray();
    }

    private static string FindAvailableName(
        string originalName,
        HashSet<string> usedNames,
        Dictionary<string, int> remainingOriginalNames)
    {
        var (stem, extension) = SplitFileName(originalName);
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{stem} ({suffix}){extension}";
            if (!usedNames.Contains(candidate) && !remainingOriginalNames.ContainsKey(candidate))
            {
                return candidate;
            }
        }
    }

    private static (string Stem, string Extension) SplitFileName(string fileName)
    {
        var finalDot = fileName.LastIndexOf('.');
        return finalDot > 0 && finalDot < fileName.Length - 1
            ? (fileName[..finalDot], fileName[finalDot..])
            : (fileName, string.Empty);
    }

    private static string GetSafeFileName(string relativePath)
    {
        var separator = relativePath.LastIndexOf('/');
        var fileName = separator >= 0 ? relativePath[(separator + 1)..] : relativePath;
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
        {
            throw new InvalidDataException("An archive entry has an invalid filename.");
        }

        return fileName;
    }

    private static void RemoveOne(Dictionary<string, int> counts, string key)
    {
        var count = counts[key];
        if (count == 1)
        {
            counts.Remove(key);
        }
        else
        {
            counts[key] = count - 1;
        }
    }
}
