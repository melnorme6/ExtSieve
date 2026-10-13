using ExtSieve.App.Services;

namespace ExtSieve.App.Tests.Services;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("1.9.0", "1.10.0")]
    [InlineData("1.1.0", "1.1.1")]
    [InlineData("1.1.1", "1.2.0")]
    [InlineData("1.2.0", "2.0.0")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-beta.11", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("1.0.0-99999999999999999999", "1.0.0-100000000000000000000")]
    [InlineData("99999999999999999999.0.0", "100000000000000000000.0.0")]
    public void PrecedenceFollowsSemVerInBothDirections(string lower, string higher)
    {
        var left = Assert.IsType<SemanticVersion>(SemanticVersion.Parse(lower));
        var right = Assert.IsType<SemanticVersion>(SemanticVersion.Parse(higher));
        Assert.True(left.CompareTo(right) < 0);
        Assert.True(right.CompareTo(left) > 0);
        Assert.Equal(0, left.CompareTo(left));
    }

    [Theory]
    [InlineData("1.1.0", "1.1.0+abcdef1")]
    [InlineData("1.1.0+one", "1.1.0+two")]
    [InlineData("1.1.0-rc.1+one", "1.1.0-rc.1+two")]
    public void BuildMetadataDoesNotAffectPrecedence(string left, string right) =>
        Assert.Equal(0, SemanticVersion.Parse(left)!.CompareTo(SemanticVersion.Parse(right)!));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.1")]
    [InlineData("1.1.0.0")]
    [InlineData("01.1.0")]
    [InlineData("1.01.0")]
    [InlineData("1.1.00")]
    [InlineData("1.1.0-01")]
    [InlineData("1.1.0-rc.01")]
    [InlineData("1.1.0+")]
    [InlineData("1.1.0-")]
    [InlineData("1.1.0-rc..1")]
    [InlineData("1.1.0+build..1")]
    [InlineData(" 1.1.0")]
    [InlineData("1.1.0\n")]
    [InlineData("1.1.0-béta")]
    public void InvalidVersionsAreRejected(string? value) => Assert.Null(SemanticVersion.Parse(value));
}
