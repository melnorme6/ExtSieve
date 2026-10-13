using System.IO.Compression;
using ExtSieve.Core.Models;
using ExtSieve.Core.Services;
using ExtSieve.Core.Tests.Support;

namespace ExtSieve.Core.Tests.Services;

public sealed class FolderExclusionTests
{
    [Fact]
    public async Task ExcludedDirectoryIsNeverEnumeratedAndSiblingsRemain()
    {
        using var tree = new TemporaryFileTree();
        tree.CreateFile("generated/deep/hidden.bin", [1, 2, 3]);
        tree.CreateFile("generated-sibling/visible.txt", [4, 5]);
        tree.CreateFile("root.txt", [6]);
        var source = new RecordingEntries(Path.Combine(tree.RootPath, "generated"));
        var result = await new FolderScanner(source).ScanAsync(tree.RootPath, null,
            TestContext.Current.CancellationToken, new FolderExclusionSet(["generated", "generated/deep", "missing"]));

        Assert.Equal(["generated-sibling/visible.txt", "root.txt"], result.Files.Select(file => file.RelativePath));
        Assert.Equal(new ExtensionGroup(".txt", 2, 3), Assert.Single(result.Groups));
        Assert.DoesNotContain(source.Enumerated, path => path.Contains("generated/deep", StringComparison.Ordinal));
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("assets/temp")]
    [InlineData("assets\\temp\\")]
    public async Task NestedExclusionKeepsItsParentAndOtherChildren(string exclusion)
    {
        using var tree = new TemporaryFileTree();
        tree.CreateFile("assets/temp/deep/hidden.txt", [1]);
        tree.CreateFile("assets/keep/visible.txt", [2]);
        tree.CreateFile("assets/parent.txt", [3]);
        var result = await new FolderScanner().ScanAsync(tree.RootPath, null,
            TestContext.Current.CancellationToken, new FolderExclusionSet([exclusion]));
        Assert.Equal(["assets/keep/visible.txt", "assets/parent.txt"], result.Files.Select(file => file.RelativePath));
    }

    [Theory]
    [InlineData(ArchiveMode.PreserveStructure)]
    [InlineData(ArchiveMode.Flatten)]
    public async Task ArchiveCannotIncludeFilesFromExcludedSubtree(ArchiveMode mode)
    {
        using var tree = new TemporaryFileTree();
        tree.CreateFile("ignored/secret.txt", [1]);
        tree.CreateFile("kept/visible.txt", [2, 3]);
        var scan = await new FolderScanner().ScanAsync(tree.RootPath, null,
            TestContext.Current.CancellationToken, new FolderExclusionSet(["ignored"]));
        var destination = Path.Combine(tree.RootPath, "output.zip");
        var plan = new ArchivePlanner().CreatePlan(new ArchiveRequest(tree.RootPath, destination,
            new HashSet<string>([".txt"]), mode, scan.Files));
        await new ZipArchiveService().CreateAsync(destination, plan, false, null, TestContext.Current.CancellationToken);
        using var zip = ZipFile.OpenRead(destination);
        Assert.Equal(mode == ArchiveMode.Flatten ? "visible.txt" : "kept/visible.txt", Assert.Single(zip.Entries).FullName);
    }

    [Fact]
    public async Task CancellationDuringScanWithExclusionsStopsEnumeration()
    {
        using var tree = new TemporaryFileTree();
        tree.CreateFile("first.txt", [1]);
        tree.CreateFile("second.txt", [2]);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FolderScanner().ScanAsync(tree.RootPath,
            new CallbackProgress<long>(_ => cancellation.Cancel()), cancellation.Token, new FolderExclusionSet(["missing"])));
    }

    [Fact]
    public async Task BrowserEnumeratesOnlyDirectDirectoriesAndRejectsTraversal()
    {
        using var tree = new TemporaryFileTree();
        tree.CreateFile("nested/deep/file.txt", [1]);
        tree.CreateFile("root.txt", [2]);
        var browser = new FolderBrowser();
        Assert.Equal(["nested"], (await browser.GetChildrenAsync(tree.RootPath, "", TestContext.Current.CancellationToken)).RelativePaths);
        Assert.Equal(["nested/deep"], (await browser.GetChildrenAsync(tree.RootPath, "nested", TestContext.Current.CancellationToken)).RelativePaths);
        Assert.True((await browser.GetChildrenAsync(tree.RootPath, "missing", TestContext.Current.CancellationToken)).HasWarnings);
        await Assert.ThrowsAsync<ArgumentException>(() => browser.GetChildrenAsync(tree.RootPath, "../outside", TestContext.Current.CancellationToken));
    }

    private sealed class RecordingEntries(string forbidden) : IDirectoryEntrySource
    {
        public List<string> Enumerated { get; } = [];
        public IEnumerable<string> EnumerateEntries(string directory)
        {
            Assert.NotEqual(forbidden, directory);
            Enumerated.Add(directory);
            return Directory.EnumerateFileSystemEntries(directory);
        }
    }
}
