namespace ExtSieve.Core.Models;

public enum ArchiveMode
{
    PreserveStructure,
    Flatten,
}

public sealed record ArchiveRequest(
    string SourceDirectory,
    string DestinationPath,
    IReadOnlySet<string> SelectedExtensionKeys,
    ArchiveMode Mode,
    IReadOnlyList<ScannedFile> Manifest);

public sealed record ArchiveEntryPlan(ScannedFile Source, string EntryPath, bool WasRenamed);

public sealed record ArchivePlan(
    IReadOnlyList<ArchiveEntryPlan> Entries,
    int CollisionRenameCount,
    long TotalSourceBytes);

public sealed record ArchiveProgress(int CompletedFiles, int TotalFiles, long CompletedBytes, long TotalBytes);

public sealed record ArchiveResult(
    string DestinationPath,
    int EntryCount,
    long SourceBytes,
    long ArchiveBytes,
    TimeSpan Duration);
