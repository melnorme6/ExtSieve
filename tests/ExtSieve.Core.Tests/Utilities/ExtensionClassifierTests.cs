using ExtSieve.Core.Utilities;

namespace ExtSieve.Core.Tests.Utilities;

public sealed class ExtensionClassifierTests
{
    [Theory]
    [InlineData("photo.JPG", ".jpg")]
    [InlineData("photo.jpg", ".jpg")]
    [InlineData("archive.tar.gz", ".gz")]
    [InlineData(".config.json", ".json")]
    [InlineData("résumé.ÉXT", ".éxt")]
    public void GetGroupKeyReturnsNormalizedFinalExtension(string fileName, string expected)
    {
        Assert.Equal(expected, ExtensionClassifier.GetGroupKey(fileName));
    }

    [Theory]
    [InlineData("README")]
    [InlineData(".gitignore")]
    [InlineData("file.")]
    [InlineData("資料")]
    public void GetGroupKeyReturnsEmptyKeyForExtensionlessNames(string fileName)
    {
        Assert.Equal(ExtensionClassifier.NoExtensionKey, ExtensionClassifier.GetGroupKey(fileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void GetGroupKeyRejectsEmptyNames(string fileName)
    {
        Assert.Throws<ArgumentException>(() => ExtensionClassifier.GetGroupKey(fileName));
    }
}
