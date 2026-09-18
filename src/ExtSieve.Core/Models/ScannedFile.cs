namespace ExtSieve.Core.Models;

public sealed record ScannedFile(
    string AbsolutePath,
    string RelativePath,
    string ExtensionKey,
    long Size,
    DateTimeOffset LastModifiedUtc);
