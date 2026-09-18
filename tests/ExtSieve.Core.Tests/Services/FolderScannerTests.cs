using ExtSieve.Core.Models;
using ExtSieve.Core.Services;
using ExtSieve.Core.Tests.Support;

namespace ExtSieve.Core.Tests.Services;

public sealed class FolderScannerTests
{
    [Fact]
    public async Task ScanBuildsDeterministicManifestGroupsAndProgress()
    {
        using var tree = new TemporaryFileTree();
        tree.CreateFile("zeta.TXT", [1, 2, 3]);
        tree.CreateFile(Path.Combine("Nested", "alpha.txt"), [4, 5]);
        tree.CreateFile(Path.Combine("Nested", "README"), [6]);
        var progressValues = new List<long>();
        var scanner = new FolderScanner();

        var result = await scanner.ScanAsync(
            tree.RootPath,
            new CallbackProgress<long>(progressValues.Add),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Files.Count);
        Assert.Equal(["Nested/alpha.txt", "Nested/README", "zeta.TXT"],
            result.Files.Select(file => file.RelativePath));
        Assert.Equal([1L, 2L, 3L], progressValues);
        Assert.Empty(result.Warnings);
        Assert.Collection(
            result.Groups,
            group => Assert.Equal(new ExtensionGroup(string.Empty, 1, 1), group),
            group => Assert.Equal(new ExtensionGroup(".txt", 2, 5), group));
        Assert.True(result.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task EmptyTreeReturnsEmptyResult()
    {
        using var tree = new TemporaryFileTree();
        var scanner = new FolderScanner();

        var result = await scanner.ScanAsync(
            tree.RootPath,
            discoveredFileProgress: null,
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Files);
        Assert.Empty(result.Groups);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task CancellationStopsScan()
    {
        using var tree = new TemporaryFileTree();
        tree.CreateFile("file.txt", [1]);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var scanner = new FolderScanner();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scanner.ScanAsync(tree.RootPath, null, cancellation.Token));
    }

    [Fact]
    public async Task DirectoryLinkIsSkippedWhenPlatformAllowsCreation()
    {
        using var tree = new TemporaryFileTree();
        var source = tree.CreateDirectory("Source");
        var outside = tree.CreateDirectory("Outside");
        File.WriteAllText(Path.Combine(outside, "outside.txt"), "outside");
        var link = Path.Combine(source, "LinkedOutside");

        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            Assert.Skip(
                $"Symbolic-link creation is unavailable in this test environment ({exception.GetType().Name}).");
        }

        var scanner = new FolderScanner();
        var result = await scanner.ScanAsync(
            source,
            discoveredFileProgress: null,
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Files);
        Assert.Contains(result.Warnings, warning => warning.Kind == ScanWarningKind.LinkSkipped);
    }

    [Fact]
    public async Task MissingSourceIsRejected()
    {
        var scanner = new FolderScanner();
        var missing = Path.Combine(Path.GetTempPath(), $"ExtSieve.Missing.{Guid.NewGuid():N}");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => scanner.ScanAsync(missing, null, TestContext.Current.CancellationToken));
    }
}
