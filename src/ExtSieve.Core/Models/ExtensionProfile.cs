namespace ExtSieve.Core.Models;

public sealed record ExtensionProfile(string Name, IReadOnlyList<string> ExtensionKeys)
{
    public static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim();
        if (normalized.Length > 80 || normalized.Any(char.IsControl))
            throw new ArgumentException("Profile names must contain at most 80 characters and no control characters.", nameof(name));
        return normalized;
    }

    public static ExtensionProfile Create(string name, IEnumerable<string> extensionKeys)
    {
        ArgumentNullException.ThrowIfNull(extensionKeys);
        var keys = extensionKeys.Select(NormalizeExtensionKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new ExtensionProfile(NormalizeName(name), Array.AsReadOnly(keys));
    }

    public static string NormalizeExtensionKey(string key)
    {
        if (key is null || (key.Length > 0 && (key.Length < 2 || key[0] != '.'
            || key[1..].Contains('.') || key.Any(character => character is '/' or '\\' or ':' || char.IsControl(character)))))
            throw new ArgumentException("Invalid extension key.", nameof(key));
        return key.ToLowerInvariant();
    }

    public static string NormalizeExtensionInput(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        var value = input.Trim();
        return NormalizeExtensionKey(value.StartsWith('.') ? value : "." + value);
    }
}
