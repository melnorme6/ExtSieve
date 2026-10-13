using ExtSieve.Core.Models;

namespace ExtSieve.Core.Tests.Utilities;

public sealed class FolderExclusionSetTests
{
    [Fact]
    public void NormalizesSeparatorsDuplicatesAndRedundantChildren()
    {
        var exclusions = new FolderExclusionSet(["src\\obj\\nested", "src/obj/", "src/obj", "assets/temp"]);
        Assert.Equal(["assets/temp", "src/obj"], exclusions.Paths);
        Assert.True(exclusions.Contains("src/obj/deep"));
        Assert.False(exclusions.Contains("src/object"));
        Assert.False(exclusions.Contains("src"));
    }

    [Fact]
    public void CaseComparisonFollowsThePlatform()
    {
        var exclusions = new FolderExclusionSet(["Build"]);
        Assert.Equal(OperatingSystem.IsWindows(), exclusions.Contains("build/deep"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("../outside")]
    [InlineData("a/../b")]
    [InlineData("/absolute")]
    [InlineData("C:\\absolute")]
    [InlineData("\\\\server\\share")]
    [InlineData("a//b")]
    [InlineData("**/bin")]
    [InlineData("temp?")]
    public void UnsafeOrPatternPathsAreRejected(string path) =>
        Assert.ThrowsAny<ArgumentException>(() => new FolderExclusionSet([path]));
}
