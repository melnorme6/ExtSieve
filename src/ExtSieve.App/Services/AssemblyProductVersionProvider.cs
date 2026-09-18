using System.Reflection;

namespace ExtSieve.App.Services;

public sealed class AssemblyProductVersionProvider : IProductVersionProvider
{
    public AssemblyProductVersionProvider(Assembly assembly)
        : this(
            GetInformationalVersion(assembly),
            assembly.GetName().Version)
    {
    }

    public AssemblyProductVersionProvider(string informationalVersion)
        : this(informationalVersion, null)
    {
    }

    private AssemblyProductVersionProvider(string? informationalVersion, Version? assemblyVersion)
    {
        InformationalVersion = string.IsNullOrWhiteSpace(informationalVersion)
            ? assemblyVersion?.ToString(3) ?? "0.0.0"
            : informationalVersion;
        PublicVersion = StripBuildMetadata(InformationalVersion);
    }

    public string InformationalVersion { get; }

    public string PublicVersion { get; }

    private static string? GetInformationalVersion(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    }

    private static string StripBuildMetadata(string informationalVersion)
    {
        var metadataSeparator = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return metadataSeparator < 0
            ? informationalVersion
            : informationalVersion[..metadataSeparator];
    }
}
