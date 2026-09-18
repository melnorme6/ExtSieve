namespace ExtSieve.Core.Utilities;

public static class ExtensionClassifier
{
    public const string NoExtensionKey = "";

    public static string GetGroupKey(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var finalDot = fileName.LastIndexOf('.');
        if (finalDot <= 0 || finalDot == fileName.Length - 1)
        {
            return NoExtensionKey;
        }

        return fileName[finalDot..].ToLowerInvariant();
    }
}
