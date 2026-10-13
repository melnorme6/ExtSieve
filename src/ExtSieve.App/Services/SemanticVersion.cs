using System.Text.RegularExpressions;

namespace ExtSieve.App.Services;

internal sealed partial class SemanticVersion
{
    private readonly string[] _numbers;
    private readonly string[] _prerelease;

    private SemanticVersion(string value, string[] numbers, string[] prerelease)
    {
        Value = value.Split('+')[0];
        _numbers = numbers;
        _prerelease = prerelease;
    }

    public string Value { get; }
    public bool IsPrerelease => _prerelease.Length > 0;

    public static SemanticVersion? Parse(string? value)
    {
        if (value is null) return null;
        var match = VersionPattern().Match(value);
        if (!match.Success) return null;

        var prerelease = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
        if (prerelease.Any(identifier => IsNumeric(identifier) && identifier.Length > 1 && identifier[0] == '0'))
            return null;

        return new SemanticVersion(value,
            [match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value], prerelease);
    }

    public int CompareTo(SemanticVersion other)
    {
        for (var index = 0; index < _numbers.Length; index++)
        {
            var comparison = CompareNumeric(_numbers[index], other._numbers[index]);
            if (comparison != 0) return comparison;
        }

        if (!IsPrerelease || !other.IsPrerelease)
            return IsPrerelease.CompareTo(other.IsPrerelease) * -1;

        for (var index = 0; index < Math.Min(_prerelease.Length, other._prerelease.Length); index++)
        {
            var left = _prerelease[index];
            var right = other._prerelease[index];
            var leftNumeric = IsNumeric(left);
            var rightNumeric = IsNumeric(right);
            var comparison = leftNumeric && rightNumeric ? CompareNumeric(left, right)
                : leftNumeric != rightNumeric ? leftNumeric ? -1 : 1
                : string.CompareOrdinal(left, right);
            if (comparison != 0) return comparison;
        }

        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }

    private static bool IsNumeric(string identifier) => identifier.All(char.IsAsciiDigit);

    // SemVer numeric identifiers have no fixed integer-size limit.
    private static int CompareNumeric(string left, string right) => left.Length == right.Length
        ? string.CompareOrdinal(left, right) : left.Length.CompareTo(right.Length);

    [GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex VersionPattern();
}
