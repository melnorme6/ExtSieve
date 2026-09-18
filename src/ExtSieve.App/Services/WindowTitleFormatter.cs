namespace ExtSieve.App.Services;

public static class WindowTitleFormatter
{
    public static string Format(string productName, string publicVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicVersion);
        return $"{productName} {publicVersion}";
    }
}
