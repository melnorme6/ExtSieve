using ExtSieve.Core.Models;
using ExtSieve.Core.Services;
using ExtSieve.Core.Utilities;

namespace ExtSieve.Core.Tests.Services;

public sealed class ArchivePlannerTests
{
    private readonly string _sourceRoot = Path.Combine(
        Path.GetTempPath(),
        $"ExtSieve.Planner.{Guid.NewGuid():N}");

    [Fact]
    public void PreserveModeIncludesSelectedGroupsAndExcludesDestination()
    {
        var destination = Path.Combine(_sourceRoot, "output.zip");
        var request = Request(
            ArchiveMode.PreserveStructure,
            destination,
            [".txt", ".zip"],
            File("Nested/report.txt", 10),
            File("image.png", 20),
            File("output.zip", 30));

        var plan = new ArchivePlanner().CreatePlan(request);

        var entry = Assert.Single(plan.Entries);
        Assert.Equal("Nested/report.txt", entry.EntryPath);
        Assert.False(entry.WasRenamed);
        Assert.Equal(10, plan.TotalSourceBytes);
        Assert.Equal(0, plan.CollisionRenameCount);
    }

    [Fact]
    public void FlattenModeResolvesCaseInsensitiveCollisionsDeterministically()
    {
        var request = Request(
            ArchiveMode.Flatten,
            Path.Combine(_sourceRoot, "output.zip"),
            [".txt"],
            File("c/NAME.txt", 3),
            File("a/name.txt", 1),
            File("b/name.txt", 2));

        var plan = new ArchivePlanner().CreatePlan(request);

        Assert.Equal(["name.txt", "name (2).txt", "NAME (3).txt"],
            plan.Entries.Select(entry => entry.EntryPath));
        Assert.Equal(2, plan.CollisionRenameCount);
        Assert.Equal(6, plan.TotalSourceBytes);
    }

    [Fact]
    public void FlattenModeReservesExistingGeneratedStyleNames()
    {
        var request = Request(
            ArchiveMode.Flatten,
            Path.Combine(_sourceRoot, "output.zip"),
            [".txt"],
            File("a/name.txt"),
            File("b/name.txt"),
            File("c/name (2).txt"));

        var plan = new ArchivePlanner().CreatePlan(request);

        Assert.Equal(["name.txt", "name (3).txt", "name (2).txt"],
            plan.Entries.Select(entry => entry.EntryPath));
    }

    [Fact]
    public void FlattenModeSuffixesExtensionlessAndDotfileNames()
    {
        var request = Request(
            ArchiveMode.Flatten,
            Path.Combine(_sourceRoot, "output.zip"),
            [ExtensionClassifier.NoExtensionKey],
            File("a/README"),
            File("b/README"),
            File("c/.gitignore"),
            File("d/.gitignore"));

        var plan = new ArchivePlanner().CreatePlan(request);

        Assert.Equal(["README", "README (2)", ".gitignore", ".gitignore (2)"],
            plan.Entries.Select(entry => entry.EntryPath));
    }

    [Fact]
    public void PlanIsIndependentOfManifestEnumerationOrder()
    {
        var files = new[] { File("z/file.txt"), File("a/file.txt"), File("m/other.txt") };
        var planner = new ArchivePlanner();

        var first = planner.CreatePlan(Request(
            ArchiveMode.Flatten,
            Path.Combine(_sourceRoot, "output.zip"),
            [".txt"],
            files));
        var second = planner.CreatePlan(Request(
            ArchiveMode.Flatten,
            Path.Combine(_sourceRoot, "output.zip"),
            [".txt"],
            files.Reverse().ToArray()));

        Assert.Equal(
            first.Entries.Select(entry => (entry.Source.RelativePath, entry.EntryPath)),
            second.Entries.Select(entry => (entry.Source.RelativePath, entry.EntryPath)));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("safe/../../outside.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/absolute.txt")]
    public void UnsafeRelativeEntryPathIsRejected(string relativePath)
    {
        var file = new ScannedFile(
            Path.Combine(_sourceRoot, "safe.txt"),
            relativePath,
            ".txt",
            1,
            DateTimeOffset.UnixEpoch);
        var request = Request(
            ArchiveMode.PreserveStructure,
            Path.Combine(_sourceRoot, "output.zip"),
            [".txt"],
            file);

        Assert.Throws<InvalidDataException>(() => new ArchivePlanner().CreatePlan(request));
    }

    [Fact]
    public void FileOutsideSourceRootIsRejected()
    {
        var outside = new ScannedFile(
            Path.Combine(Path.GetTempPath(), $"outside.{Guid.NewGuid():N}.txt"),
            "outside.txt",
            ".txt",
            1,
            DateTimeOffset.UnixEpoch);
        var request = Request(
            ArchiveMode.PreserveStructure,
            Path.Combine(_sourceRoot, "output.zip"),
            [".txt"],
            outside);

        Assert.Throws<InvalidDataException>(() => new ArchivePlanner().CreatePlan(request));
    }

    [Fact]
    public void RelativePathThatDoesNotMatchAbsolutePathIsRejected()
    {
        var file = new ScannedFile(
            Path.Combine(_sourceRoot, "actual.txt"),
            "different.txt",
            ".txt",
            1,
            DateTimeOffset.UnixEpoch);
        var request = Request(
            ArchiveMode.PreserveStructure,
            Path.Combine(_sourceRoot, "output.zip"),
            [".txt"],
            file);

        Assert.Throws<InvalidDataException>(() => new ArchivePlanner().CreatePlan(request));
    }

    private ArchiveRequest Request(
        ArchiveMode mode,
        string destination,
        string[] selectedKeys,
        params ScannedFile[] files)
    {
        return new ArchiveRequest(
            _sourceRoot,
            destination,
            new HashSet<string>(selectedKeys, StringComparer.OrdinalIgnoreCase),
            mode,
            files);
    }

    private ScannedFile File(string relativePath, long size = 1)
    {
        var nativeRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var absolutePath = Path.Combine(_sourceRoot, nativeRelativePath);
        var fileName = relativePath[(relativePath.LastIndexOf('/') + 1)..];
        return new ScannedFile(
            absolutePath,
            relativePath,
            ExtensionClassifier.GetGroupKey(fileName),
            size,
            DateTimeOffset.UnixEpoch);
    }
}
