using System.Globalization;

namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>Shared formatting for <see cref="EntityProperty"/> values, so every provider reads alike.</summary>
public static class EntityPropertyFormat
{
    // TimeSpan.MaxValue (and anything near it) is how brokers say "no limit".
    private static readonly TimeSpan Never = TimeSpan.FromDays(10_000);

    public static string Flag(bool value) => value ? "yes" : "no";

    public static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Duration(TimeSpan value)
    {
        if (value >= Never)
        {
            return "never";
        }

        List<string> parts = [];
        if (value.Days > 0)
        {
            parts.Add($"{value.Days} d");
        }

        if (value.Hours > 0)
        {
            parts.Add($"{value.Hours} h");
        }

        if (value.Minutes > 0)
        {
            parts.Add($"{value.Minutes} min");
        }

        if (value.Seconds > 0)
        {
            parts.Add($"{value.Seconds} s");
        }

        if (value.Milliseconds > 0 && value < TimeSpan.FromMinutes(1))
        {
            parts.Add($"{value.Milliseconds} ms");
        }

        return parts.Count == 0 ? "0 s" : string.Join(' ', parts);
    }

    public static string Bytes(long value) => value switch
    {
        >= 1L << 30 => string.Create(CultureInfo.InvariantCulture, $"{value / (double)(1L << 30):0.##} GB"),
        >= 1L << 20 => string.Create(CultureInfo.InvariantCulture, $"{value / (double)(1L << 20):0.##} MB"),
        >= 1L << 10 => string.Create(CultureInfo.InvariantCulture, $"{value / (double)(1L << 10):0.##} KB"),
        _ => $"{value} B",
    };
}
