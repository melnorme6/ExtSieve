using System.Globalization;

namespace ExtSieve.App.ViewModels;

public static class ByteSizeFormatter
{
    private static readonly string[] Units = ["B", "KiB", "MiB", "GiB", "TiB"];

    public static string Format(long bytes, CultureInfo culture)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        ArgumentNullException.ThrowIfNull(culture);

        decimal value = bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < Units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        var format = unitIndex == 0 || value >= 100 ? "N0" : "N1";
        return $"{value.ToString(format, culture)} {Units[unitIndex]}";
    }
}
