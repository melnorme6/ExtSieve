namespace ExtSieve.Core.Models;

public enum ScanWarningKind
{
    InaccessibleDirectory,
    UnreadableFile,
    LinkSkipped,
}

public sealed record ScanWarning(ScanWarningKind Kind, string Path);

public sealed record ScanResult(
    IReadOnlyList<ScannedFile> Files,
    IReadOnlyList<ExtensionGroup> Groups,
    IReadOnlyList<ScanWarning> Warnings,
    TimeSpan Duration);
