using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using ExtSieve.Core.Models;
using ExtSieve.Core.Utilities;

namespace ExtSieve.Core.Services;

public sealed class ZipArchiveService : IZipArchiveService
{
    private const int CopyBufferSize = 81920;
    public async Task<ArchiveResult> CreateAsync(
        string destinationPath,
        ArchivePlan plan,
        bool replaceExistingDestination,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(plan);

        var destination = Path.GetFullPath(destinationPath);
        var entries = ValidatePlan(plan, destination);
        var destinationDirectory = Path.GetDirectoryName(destination)
            ?? throw new InvalidDataException("The archive destination has no parent directory.");
        if (!Directory.Exists(destinationDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Archive destination directory does not exist: {destinationDirectory}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var destinationSnapshot = CaptureDestination(
            destination,
            replaceExistingDestination);
        var temporaryPath = CreateTemporaryPath(destinationDirectory, Path.GetFileName(destination));
        var stopwatch = Stopwatch.StartNew();

        try
        {
            progress?.Report(new ArchiveProgress(0, entries.Length, 0, plan.TotalSourceBytes));
            await WriteArchiveAsync(
                    temporaryPath,
                    entries,
                    plan.TotalSourceBytes,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            await ValidateArchiveAsync(temporaryPath, entries, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            EnsureDestinationUnchanged(destination, destinationSnapshot);
            PromoteTemporaryArchive(
                temporaryPath,
                destination,
                destinationSnapshot is not null);
            stopwatch.Stop();
            var archiveBytes = new FileInfo(destination).Length;
            return new ArchiveResult(
                destination,
                entries.Length,
                plan.TotalSourceBytes,
                archiveBytes,
                stopwatch.Elapsed);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static ArchiveEntryPlan[] ValidatePlan(ArchivePlan plan, string destination)
    {
        ArgumentNullException.ThrowIfNull(plan.Entries);
        if (plan.TotalSourceBytes < 0)
        {
            throw new InvalidDataException("Archive source byte totals cannot be negative.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedEntries = new ArchiveEntryPlan[plan.Entries.Count];
        long totalBytes = 0;
        for (var index = 0; index < plan.Entries.Count; index++)
        {
            var entry = plan.Entries[index]
                ?? throw new InvalidDataException("An archive plan contains a null entry.");
            ArgumentNullException.ThrowIfNull(entry.Source);
            if (entry.Source.Size < 0)
            {
                throw new InvalidDataException("Archive source file sizes cannot be negative.");
            }

            var sourcePath = Path.GetFullPath(entry.Source.AbsolutePath);
            if (PathRules.PlatformComparer.Equals(sourcePath, destination))
            {
                throw new InvalidDataException("The destination archive cannot be a source entry.");
            }

            var entryPath = PathRules.NormalizeAndValidateArchiveEntryPath(entry.EntryPath);
            if (!names.Add(entryPath))
            {
                throw new InvalidDataException(
                    "Archive entry names must be unique when compared case-insensitively.");
            }

            checked
            {
                totalBytes += entry.Source.Size;
            }

            normalizedEntries[index] = entry with
            {
                Source = entry.Source with { AbsolutePath = sourcePath },
                EntryPath = entryPath,
            };
        }

        if (totalBytes != plan.TotalSourceBytes)
        {
            throw new InvalidDataException("The archive plan source byte total is inconsistent.");
        }

        return normalizedEntries;
    }

    private static async Task WriteArchiveAsync(
        string temporaryPath,
        ArchiveEntryPlan[] entries,
        long totalBytes,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        long completedBytes = 0;
        var completedFiles = 0;
        foreach (var plannedEntry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var source = OpenVerifiedSource(plannedEntry.Source);
            var archiveEntry = archive.CreateEntry(
                plannedEntry.EntryPath,
                CompressionLevel.Optimal);
            archiveEntry.LastWriteTime = ClampZipTimestamp(plannedEntry.Source.LastModifiedUtc);
            await using var entryStream = archiveEntry.Open();
            completedBytes = await CopySourceAsync(
                    source,
                    entryStream,
                    plannedEntry.Source,
                    completedBytes,
                    completedFiles,
                    entries.Length,
                    totalBytes,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            VerifySourceUnchanged(plannedEntry.Source, source);
            completedFiles++;
            progress?.Report(new ArchiveProgress(
                completedFiles,
                entries.Length,
                completedBytes,
                totalBytes));
        }
    }

    private static FileStream OpenVerifiedSource(ScannedFile source)
    {
        try
        {
            var attributes = File.GetAttributes(source.AbsolutePath);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new IOException("The source is not a regular file.");
            }

            var stream = new FileStream(
                source.AbsolutePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            attributes = File.GetAttributes(source.AbsolutePath);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
                || stream.Length != source.Size
                || File.GetLastWriteTimeUtc(source.AbsolutePath) != source.LastModifiedUtc.UtcDateTime)
            {
                stream.Dispose();
                throw new IOException("The source changed after it was scanned.");
            }

            return stream;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Unable to archive source file '{source.AbsolutePath}'.",
                exception);
        }
    }

    private static async Task<long> CopySourceAsync(
        FileStream sourceStream,
        Stream destinationStream,
        ScannedFile source,
        long completedBytes,
        int completedFiles,
        int totalFiles,
        long totalBytes,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            while (true)
            {
                int bytesRead;
                try
                {
                    bytesRead = await sourceStream
                        .ReadAsync(buffer.AsMemory(0, CopyBufferSize), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new IOException(
                        $"Unable to read source file '{source.AbsolutePath}'.",
                        exception);
                }

                if (bytesRead == 0)
                {
                    return completedBytes;
                }

                await destinationStream
                    .WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                    .ConfigureAwait(false);
                completedBytes += bytesRead;
                progress?.Report(new ArchiveProgress(
                    completedFiles,
                    totalFiles,
                    completedBytes,
                    totalBytes));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void VerifySourceUnchanged(ScannedFile source, FileStream stream)
    {
        try
        {
            if (stream.Length != source.Size
                || stream.Position != source.Size
                || File.GetLastWriteTimeUtc(source.AbsolutePath) != source.LastModifiedUtc.UtcDateTime)
            {
                throw new IOException("The source changed while the archive was being created.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Unable to verify source file '{source.AbsolutePath}'.",
                exception);
        }
    }

    private static async Task ValidateArchiveAsync(
        string temporaryPath,
        ArchiveEntryPlan[] expectedEntries,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            temporaryPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count != expectedEntries.Length)
        {
            throw new InvalidDataException("The completed ZIP entry count is invalid.");
        }

        for (var index = 0; index < archive.Entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.Entries[index];
            if (!string.Equals(
                    entry.FullName,
                    expectedEntries[index].EntryPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("The completed ZIP entry order or name is invalid.");
            }

            await using var entryStream = entry.Open();
            await entryStream.CopyToAsync(Stream.Null, cancellationToken).ConfigureAwait(false);
        }
    }

    private static DateTimeOffset ClampZipTimestamp(DateTimeOffset timestamp)
    {
        var localTimestamp = timestamp.ToLocalTime();
        if (localTimestamp.Year < 1980)
        {
            return new DateTimeOffset(1980, 1, 1, 0, 0, 0, localTimestamp.Offset);
        }

        return localTimestamp.Year > 2107
            ? new DateTimeOffset(2107, 12, 31, 23, 59, 58, localTimestamp.Offset)
            : localTimestamp;
    }

    private static string CreateTemporaryPath(string directory, string destinationFileName)
    {
        return Path.Combine(directory, $".{destinationFileName}.{Guid.NewGuid():N}.tmp");
    }

    private static DestinationSnapshot? CaptureDestination(
        string destination,
        bool replaceExistingDestination)
    {
        if (!File.Exists(destination))
        {
            if (Directory.Exists(destination))
            {
                throw new IOException("The archive destination is a directory.");
            }

            return null;
        }

        if (!replaceExistingDestination)
        {
            throw new IOException("The archive destination exists, but replacement was not approved.");
        }

        var attributes = File.GetAttributes(destination);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new IOException("The archive destination must be a regular file.");
        }

        var file = new FileInfo(destination);
        return new DestinationSnapshot(file.Length, file.LastWriteTimeUtc);
    }

    private static void EnsureDestinationUnchanged(
        string destination,
        DestinationSnapshot? expected)
    {
        if (expected is null)
        {
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                throw new IOException("The archive destination appeared while the archive was being created.");
            }

            return;
        }

        if (!File.Exists(destination))
        {
            throw new IOException("The archive destination changed while the archive was being created.");
        }

        var attributes = File.GetAttributes(destination);
        var file = new FileInfo(destination);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
            || file.Length != expected.Length
            || file.LastWriteTimeUtc != expected.LastWriteTimeUtc)
        {
            throw new IOException("The archive destination changed while the archive was being created.");
        }
    }

    private static void PromoteTemporaryArchive(
        string temporaryPath,
        string destination,
        bool replacingExistingDestination)
    {
        if (replacingExistingDestination)
        {
            File.Replace(temporaryPath, destination, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temporaryPath, destination);
        }
    }

    private sealed record DestinationSnapshot(long Length, DateTime LastWriteTimeUtc);
}
