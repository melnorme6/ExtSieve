using System.IO.Compression;
using ExtSieve.Core.Models;
using ExtSieve.Core.Services;
using ExtSieve.Core.Tests.Support;
using ExtSieve.Core.Utilities;

namespace ExtSieve.Core.Tests.Services;

public sealed class ZipArchiveServiceTests
{
    [Fact]
    public async Task CreateAsyncWritesOrderedEntriesContentTimestampsAndProgress()
    {
        using var tree = new TemporaryFileTree();
        var timestamp = new DateTime(2024, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        var binaryContent = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
        var binary = CreateScannedFile(tree, "nested/data.bin", binaryContent, timestamp);
        var empty = CreateScannedFile(tree, "empty.txt", [], timestamp);
        var unicode = CreateScannedFile(tree, "città/è.txt", "contenuto"u8.ToArray(), timestamp);
        var destination = Path.Combine(tree.RootPath, "risultato-è.zip");
        var progressUpdates = new List<ArchiveProgress>();

        var result = await new ZipArchiveService().CreateAsync(
            destination,
            Plan(
                new ArchiveEntryPlan(unicode, "città/è.txt", false),
                new ArchiveEntryPlan(binary, "nested/data.bin", false),
                new ArchiveEntryPlan(empty, "empty.txt", false)),
            replaceExistingDestination: false,
            new CallbackProgress<ArchiveProgress>(progressUpdates.Add),
            TestContext.Current.CancellationToken);

        Assert.Equal(Path.GetFullPath(destination), result.DestinationPath);
        Assert.Equal(3, result.EntryCount);
        Assert.Equal(binaryContent.Length + "contenuto"u8.Length, result.SourceBytes);
        Assert.True(result.ArchiveBytes > 0);
        Assert.True(result.Duration >= TimeSpan.Zero);

        using var archive = ZipFile.OpenRead(destination);
        Assert.Equal(
            ["città/è.txt", "nested/data.bin", "empty.txt"],
            archive.Entries.Select(entry => entry.FullName));
        Assert.Equal("contenuto"u8.ToArray(), ReadEntry(archive.Entries[0]));
        Assert.Equal(binaryContent, ReadEntry(archive.Entries[1]));
        Assert.Empty(ReadEntry(archive.Entries[2]));
        Assert.All(archive.Entries, entry =>
            Assert.InRange(
                Math.Abs((entry.LastWriteTime.UtcDateTime - timestamp).TotalSeconds),
                0,
                2));

        Assert.NotEmpty(progressUpdates);
        Assert.Equal(new ArchiveProgress(0, 3, 0, result.SourceBytes), progressUpdates[0]);
        Assert.Equal(
            new ArchiveProgress(3, 3, result.SourceBytes, result.SourceBytes),
            progressUpdates[^1]);
        Assert.True(progressUpdates
            .Zip(progressUpdates.Skip(1))
            .All(pair => pair.First.CompletedBytes <= pair.Second.CompletedBytes));
        AssertNoTemporaryArchives(tree.RootPath, Path.GetFileName(destination));
    }

    [Fact]
    public async Task CreateAsyncReplacesExistingDestinationOnlyAfterSuccess()
    {
        using var tree = new TemporaryFileTree();
        var source = CreateScannedFile(tree, "source.txt", "new"u8.ToArray());
        var destination = tree.CreateFile("result.zip", "old"u8.ToArray());

        await new ZipArchiveService().CreateAsync(
            destination,
            Plan(new ArchiveEntryPlan(source, "source.txt", false)),
            replaceExistingDestination: true,
            progress: null,
            TestContext.Current.CancellationToken);

        using var archive = ZipFile.OpenRead(destination);
        Assert.Equal("new"u8.ToArray(), ReadEntry(Assert.Single(archive.Entries)));
        AssertNoTemporaryArchives(tree.RootPath, Path.GetFileName(destination));
    }

    [Fact]
    public async Task ExistingDestinationIsPreservedWithoutExplicitReplacementApproval()
    {
        using var tree = new TemporaryFileTree();
        var source = CreateScannedFile(tree, "source.txt", "new"u8.ToArray());
        var destination = tree.CreateFile("result.zip", "existing"u8.ToArray());

        await Assert.ThrowsAsync<IOException>(() =>
            new ZipArchiveService().CreateAsync(
                destination,
                Plan(new ArchiveEntryPlan(source, "source.txt", false)),
                replaceExistingDestination: false,
                progress: null,
                TestContext.Current.CancellationToken));

        Assert.Equal("existing"u8.ToArray(), File.ReadAllBytes(destination));
        AssertNoTemporaryArchives(tree.RootPath, Path.GetFileName(destination));
    }

    [Fact]
    public async Task DestinationThatAppearsDuringCreationIsNotOverwritten()
    {
        using var tree = new TemporaryFileTree();
        var source = CreateScannedFile(tree, "source.txt", new byte[1024]);
        var destination = Path.Combine(tree.RootPath, "result.zip");
        var progress = new CallbackProgress<ArchiveProgress>(_ =>
        {
            if (!File.Exists(destination))
            {
                File.WriteAllText(destination, "concurrent");
            }
        });

        await Assert.ThrowsAsync<IOException>(() =>
            new ZipArchiveService().CreateAsync(
                destination,
                Plan(new ArchiveEntryPlan(source, "source.txt", false)),
                replaceExistingDestination: false,
                progress,
                TestContext.Current.CancellationToken));

        Assert.Equal("concurrent", File.ReadAllText(destination));
        AssertNoTemporaryArchives(tree.RootPath, Path.GetFileName(destination));
    }

    [Fact]
    public async Task DestinationChangedDuringCreationIsNotOverwritten()
    {
        using var tree = new TemporaryFileTree();
        var source = CreateScannedFile(tree, "source.txt", new byte[1024]);
        var destination = tree.CreateFile("result.zip", "existing"u8.ToArray());
        var changed = false;
        var progress = new CallbackProgress<ArchiveProgress>(_ =>
        {
            if (changed)
            {
                return;
            }

            File.WriteAllText(destination, "concurrent replacement");
            changed = true;
        });

        await Assert.ThrowsAsync<IOException>(() =>
            new ZipArchiveService().CreateAsync(
                destination,
                Plan(new ArchiveEntryPlan(source, "source.txt", false)),
                replaceExistingDestination: true,
                progress,
                TestContext.Current.CancellationToken));

        Assert.Equal("concurrent replacement", File.ReadAllText(destination));
        AssertNoTemporaryArchives(tree.RootPath, Path.GetFileName(destination));
    }

    [Theory]
    [InlineData(ArchiveMode.PreserveStructure, "a/name.txt", "b/name.txt")]
    [InlineData(ArchiveMode.Flatten, "name.txt", "name (2).txt")]
    public async Task PlannerAndWriterProduceExpectedEntriesForBothModes(
        ArchiveMode mode,
        string firstEntry,
        string secondEntry)
    {
        using var tree = new TemporaryFileTree();
        var first = CreateScannedFile(tree, "a/name.txt", "first"u8.ToArray());
        var second = CreateScannedFile(tree, "b/name.txt", "second"u8.ToArray());
        var destination = Path.Combine(tree.RootPath, "result.zip");
        var plan = new ArchivePlanner().CreatePlan(new ArchiveRequest(
            tree.RootPath,
            destination,
            new HashSet<string>([".txt"], StringComparer.OrdinalIgnoreCase),
            mode,
            [second, first]));

        await new ZipArchiveService().CreateAsync(
            destination,
            plan,
            replaceExistingDestination: false,
            progress: null,
            TestContext.Current.CancellationToken);

        using var archive = ZipFile.OpenRead(destination);
        Assert.Equal([firstEntry, secondEntry], archive.Entries.Select(entry => entry.FullName));
        Assert.Equal("first"u8.ToArray(), ReadEntry(archive.Entries[0]));
        Assert.Equal("second"u8.ToArray(), ReadEntry(archive.Entries[1]));
    }

    [Fact]
    public async Task MissingSourcePreservesExistingDestinationAndRemovesTemporaryFile()
    {
        using var tree = new TemporaryFileTree();
        var source = CreateScannedFile(tree, "source.txt", "source"u8.ToArray());
        File.Delete(source.AbsolutePath);
        var destination = tree.CreateFile("result.zip", "previous"u8.ToArray());

        var archiveException = await Assert.ThrowsAsync<IOException>(() =>
            new ZipArchiveService().CreateAsync(
                destination,
                Plan(new ArchiveEntryPlan(source, "source.txt", false)),
                replaceExistingDestination: true,
                progress: null,
                TestContext.Current.CancellationToken));

        Assert.Contains(source.AbsolutePath, archiveException.Message, StringComparison.Ordinal);
        Assert.Equal("previous"u8.ToArray(), File.ReadAllBytes(destination));
        AssertNoTemporaryArchives(tree.RootPath, Path.GetFileName(destination));
    }

    [Fact]
    public async Task CancellationPreservesExistingDestinationAndRemovesTemporaryFile()
    {
        using var tree = new TemporaryFileTree();
        var content = new byte[2 * 1024 * 1024];
        new Random(12345).NextBytes(content);
        var source = CreateScannedFile(tree, "large.bin", content);
        var destination = tree.CreateFile("result.zip", "previous"u8.ToArray());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var progress = new CallbackProgress<ArchiveProgress>(update =>
        {
            if (update.CompletedBytes > 0)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ZipArchiveService().CreateAsync(
                destination,
                Plan(new ArchiveEntryPlan(source, "large.bin", false)),
                replaceExistingDestination: true,
                progress,
                cancellation.Token));

        Assert.Equal("previous"u8.ToArray(), File.ReadAllBytes(destination));
        AssertNoTemporaryArchives(tree.RootPath, Path.GetFileName(destination));
    }

    [Fact]
    public async Task ChangedSourcePreservesExistingDestination()
    {
        using var tree = new TemporaryFileTree();
        var source = CreateScannedFile(tree, "source.txt", "before"u8.ToArray());
        File.AppendAllText(source.AbsolutePath, " changed");
        var destination = tree.CreateFile("result.zip", "previous"u8.ToArray());

        var archiveException = await Assert.ThrowsAsync<IOException>(() =>
            new ZipArchiveService().CreateAsync(
                destination,
                Plan(new ArchiveEntryPlan(source, "source.txt", false)),
                replaceExistingDestination: true,
                progress: null,
                TestContext.Current.CancellationToken));

        Assert.Contains(source.AbsolutePath, archiveException.Message, StringComparison.Ordinal);
        Assert.Equal("previous"u8.ToArray(), File.ReadAllBytes(destination));
        AssertNoTemporaryArchives(tree.RootPath, Path.GetFileName(destination));
    }

    [Fact]
    public async Task LinkSourceIsRejectedWhenPlatformPermitsLinkCreation()
    {
        using var tree = new TemporaryFileTree();
        using var targetTree = new TemporaryFileTree();
        var target = CreateScannedFile(targetTree, "target.txt", "target"u8.ToArray());
        var linkPath = Path.Combine(tree.RootPath, "link.txt");
        try
        {
            File.CreateSymbolicLink(linkPath, target.AbsolutePath);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            Assert.Skip(
                $"Symbolic-link creation is unavailable in this test environment ({exception.GetType().Name}).");
        }

        var link = target with { AbsolutePath = linkPath, RelativePath = "link.txt" };
        var destination = tree.CreateFile("result.zip", "previous"u8.ToArray());

        var archiveException = await Assert.ThrowsAsync<IOException>(() =>
            new ZipArchiveService().CreateAsync(
                destination,
                Plan(new ArchiveEntryPlan(link, "link.txt", false)),
                replaceExistingDestination: true,
                progress: null,
                TestContext.Current.CancellationToken));

        Assert.Contains(linkPath, archiveException.Message, StringComparison.Ordinal);
        Assert.Equal("previous"u8.ToArray(), File.ReadAllBytes(destination));
        AssertNoTemporaryArchives(tree.RootPath, Path.GetFileName(destination));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/absolute.txt")]
    [InlineData("safe//empty.txt")]
    [InlineData("safe\\..\\outside.txt")]
    public async Task UnsafeEntryPathIsRejectedBeforeCreatingOutput(string entryPath)
    {
        using var tree = new TemporaryFileTree();
        var source = CreateScannedFile(tree, "source.txt", "source"u8.ToArray());
        var destination = Path.Combine(tree.RootPath, "result.zip");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ZipArchiveService().CreateAsync(
                destination,
                Plan(new ArchiveEntryPlan(source, entryPath, false)),
                replaceExistingDestination: false,
                progress: null,
                TestContext.Current.CancellationToken));

        Assert.False(File.Exists(destination));
        AssertNoTemporaryArchives(tree.RootPath, Path.GetFileName(destination));
    }

    [Fact]
    public async Task CaseInsensitiveDuplicateEntryNamesAreRejected()
    {
        using var tree = new TemporaryFileTree();
        var first = CreateScannedFile(tree, "first.txt", "first"u8.ToArray());
        var second = CreateScannedFile(tree, "second.txt", "second"u8.ToArray());
        var destination = Path.Combine(tree.RootPath, "result.zip");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ZipArchiveService().CreateAsync(
                destination,
                Plan(
                    new ArchiveEntryPlan(first, "name.txt", false),
                    new ArchiveEntryPlan(second, "NAME.TXT", true)),
                replaceExistingDestination: false,
                progress: null,
                TestContext.Current.CancellationToken));

        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task InconsistentSourceByteTotalIsRejected()
    {
        using var tree = new TemporaryFileTree();
        var source = CreateScannedFile(tree, "source.txt", "source"u8.ToArray());
        var destination = Path.Combine(tree.RootPath, "result.zip");
        var plan = new ArchivePlan(
            [new ArchiveEntryPlan(source, "source.txt", false)],
            CollisionRenameCount: 0,
            TotalSourceBytes: source.Size + 1);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ZipArchiveService().CreateAsync(
                destination,
                plan,
                replaceExistingDestination: false,
                progress: null,
                TestContext.Current.CancellationToken));

        Assert.False(File.Exists(destination));
    }

    private static ArchivePlan Plan(params ArchiveEntryPlan[] entries)
    {
        return new ArchivePlan(
            entries,
            entries.Count(entry => entry.WasRenamed),
            entries.Sum(entry => entry.Source.Size));
    }

    private static ScannedFile CreateScannedFile(
        TemporaryFileTree tree,
        string relativePath,
        byte[] content,
        DateTime? lastModifiedUtc = null)
    {
        var absolutePath = tree.CreateFile(relativePath, content);
        if (lastModifiedUtc is { } timestamp)
        {
            File.SetLastWriteTimeUtc(absolutePath, timestamp);
        }

        var file = new FileInfo(absolutePath);
        file.Refresh();
        return new ScannedFile(
            absolutePath,
            relativePath.Replace('\\', '/'),
            ExtensionClassifier.GetGroupKey(file.Name),
            file.Length,
            file.LastWriteTimeUtc);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void AssertNoTemporaryArchives(string directory, string destinationFileName)
    {
        Assert.Empty(Directory.EnumerateFiles(
            directory,
            $".{destinationFileName}.*.tmp",
            SearchOption.TopDirectoryOnly));
    }
}
