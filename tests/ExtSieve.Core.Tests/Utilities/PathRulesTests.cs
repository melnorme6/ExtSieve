using ExtSieve.Core.Utilities;

namespace ExtSieve.Core.Tests.Utilities;

public sealed class PathRulesTests
{
    [Fact]
    public void IsWithinRootAcceptsChildrenWhenRootIsVolumeRoot()
    {
        var root = Path.GetPathRoot(Path.GetTempPath());
        Assert.False(string.IsNullOrWhiteSpace(root));
        var child = Path.Combine(root, "ExtSieveBoundary", "file.txt");

        Assert.True(PathRules.IsWithinRoot(child, root));
    }

    [Fact]
    public void IsWithinRootRejectsSiblingWithSharedNamePrefix()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"ExtSieveBoundary.{Guid.NewGuid():N}");
        var root = Path.Combine(parent, "source");
        var sibling = Path.Combine(parent, "source-other", "file.txt");

        Assert.False(PathRules.IsWithinRoot(sibling, root));
    }
}
