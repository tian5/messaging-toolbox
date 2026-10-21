using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MessagingToolbox.Application.Common;

/// <summary>
/// Converts application property values between the composer's text form and the typed values
/// the broker carries. Text is always invariant-culture, so a value round-trips through prefill,
/// edit and send without changing type or meaning.
/// </summary>
public static class ApplicationPropertyValues
{
    /// <summary>The composer type for a value read from a message; types without a composer equivalent fall back to text.</summary>
    public static PropertyValueType TypeOf(object? value) => value switch
    {
        int or short or byte or sbyte or ushort => PropertyValueType.Int32,
        long or uint or ulong => PropertyValueType.Int64,
        bool => PropertyValueType.Boolean,
        double or float or decimal => PropertyValueType.Double,
        DateTime or DateTimeOffset => PropertyValueType.DateTime,
        Guid => PropertyValueType.Guid,
        _ => PropertyValueType.String,
    };

    public static string Format(object? value) => value switch
    {
        null => string.Empty,
        bool flag => flag ? "true" : "false",
        DateTime dateTime => ToUtc(dateTime).ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    public static bool TryParse(string? text, PropertyValueType type, [NotNullWhen(true)] out object? value)
    {
        string trimmed = text?.Trim() ?? string.Empty;
        value = null;
        switch (type)
        {
            case PropertyValueType.String:
                value = text ?? string.Empty;
                return true;
            case PropertyValueType.Int32 when int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int int32):
                value = int32;
                return true;
            case PropertyValueType.Int64 when long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long int64):
                value = int64;
                return true;
            case PropertyValueType.Boolean when bool.TryParse(trimmed, out bool boolean):
                value = boolean;
                return true;
            case PropertyValueType.Double when double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double number):
                value = number;
                return true;
            case PropertyValueType.DateTime when DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset timestamp):
                value = timestamp.ToUniversalTime();
                return true;
            case PropertyValueType.Guid when Guid.TryParse(trimmed, out Guid guid):
                value = guid;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Parses <paramref name="text"/> as <paramref name="type"/>; throws <see cref="FormatException"/> naming the property otherwise.</summary>
    public static object Parse(string key, string? text, PropertyValueType type)
        => TryParse(text, type, out object? value)
            ? value
            : throw new FormatException($"Application property '{key}': '{text}' is not a valid {type} value.");

    /// <summary>
    /// Validation-time check: a value with a placeholder (<c>~…~</c>) is only known after
    /// rendering, so it is checked at send time instead.
    /// </summary>
    public static bool IsValidLiteral(string? text, PropertyValueType type)
        => text?.Contains('~') == true || TryParse(text, type, out _);

    /// <summary>
    /// Builds the typed property bag of one outgoing message. <paramref name="render"/> expands
    /// placeholders first, so a templated value (e.g. <c>~randomInt~</c>) can still be sent typed.
    /// Properties missing from <paramref name="types"/> are text.
    /// </summary>
    public static Dictionary<string, object?> ToTyped(
        IReadOnlyDictionary<string, string> properties,
        IReadOnlyDictionary<string, PropertyValueType>? types,
        Func<string, string> render)
        => properties.ToDictionary(
            pair => pair.Key,
            pair => (object?)Parse(pair.Key, render(pair.Value), types?.GetValueOrDefault(pair.Key) ?? PropertyValueType.String));

    // A broker timestamp without a kind is UTC.
    private static DateTimeOffset ToUtc(DateTime dateTime)
        => new(dateTime.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc) : dateTime.ToUniversalTime());
}
