using System.Reflection;

namespace ExtSieve.App.Services;

public sealed class AssemblyProductAboutProvider : IProductAboutProvider
{
    private const int ShortCommitLength = 7;

    public AssemblyProductAboutProvider(Assembly assembly)
        : this(assembly, AppContext.BaseDirectory)
    {
    }

    public AssemblyProductAboutProvider(Assembly assembly, string applicationDirectory)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);

        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .GroupBy(attribute => attribute.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value,
                StringComparer.Ordinal);
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        ProductName = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product
            ?? assembly.GetName().Name
            ?? string.Empty;
        BuildInfo = GetSafeBuildInfo(informationalVersion);
        LicenseName = GetLicenseName(GetMetadata(metadata, "PackageLicenseExpression"));
        ReleasePageUri = GetPublicReleasePageUri(GetMetadata(metadata, "PublicReleaseUrl"));
        SupportPageUri = GetPublicSupportPageUri(GetMetadata(metadata, "SupportUrl"));
        LicenseDocumentPath = GetExistingDocumentPath(applicationDirectory, "LICENSE");
        ThirdPartyNoticesPath = GetExistingDocumentPath(
            applicationDirectory,
            "THIRD_PARTY_NOTICES.md");
    }

    public string ProductName { get; }

    public string? BuildInfo { get; }

    public string? LicenseName { get; }

    public string? LicenseDocumentPath { get; }

    public Uri? ReleasePageUri { get; }

    public Uri? SupportPageUri { get; }

    public string? ThirdPartyNoticesPath { get; }

    public static string? GetSafeBuildInfo(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

        var separator = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        if (separator < 0 || separator == informationalVersion.Length - 1)
        {
            return null;
        }

        foreach (var token in informationalVersion[(separator + 1)..]
                     .Split(['.', '-'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length >= ShortCommitLength
                && token.Length <= 40
                && token.All(Uri.IsHexDigit))
            {
                return token[..Math.Min(ShortCommitLength, token.Length)].ToLowerInvariant();
            }
        }

        return null;
    }

    public static Uri? GetPublicReleasePageUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 3
            && string.Equals(segments[2], "releases", StringComparison.OrdinalIgnoreCase)
            ? uri
            : null;
    }

    public static Uri? GetPublicSupportPageUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "ko-fi.com", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 1 ? uri : null;
    }

    private static string? GetMetadata(
        Dictionary<string, string?> metadata,
        string key) => metadata.TryGetValue(key, out var value) ? value : null;

    private static string? GetLicenseName(string? licenseExpression) => licenseExpression switch
    {
        "MIT" => "MIT License",
        null or "" => null,
        _ => licenseExpression,
    };

    private static string? GetExistingDocumentPath(string applicationDirectory, string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(applicationDirectory, fileName));
        return File.Exists(path) ? path : null;
    }
}
