using System.Globalization;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Web.Components.Management;

/// <summary>
/// Form state for provider-described options (ADR 21/22): booleans as flags, everything else as
/// the text the user typed, converted to the declared value types on submit.
/// </summary>
public sealed class EntityOptionForm
{
    public Dictionary<string, bool> Flags { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Texts { get; } = new(StringComparer.Ordinal);

    /// <summary>Fills the form from typed values; options without a value start empty.</summary>
    public void Load(IReadOnlyList<EntityOptionDescriptor> options, IReadOnlyDictionary<string, object?> values)
    {
        Flags.Clear();
        Texts.Clear();
        foreach (EntityOptionDescriptor option in options)
        {
            object? value = values.GetValueOrDefault(option.Key);
            if (option.Type == EntityOptionType.Boolean)
            {
                Flags[option.Key] = value is true;
            }
            else
            {
                Texts[option.Key] = Format(value);
            }
        }
    }

    /// <summary>Fills the form with each option's default.</summary>
    public void LoadDefaults(IReadOnlyList<EntityOptionDescriptor> options)
        => Load(options, options.ToDictionary(option => option.Key, option => option.DefaultValue, StringComparer.Ordinal));

    /// <summary>
    /// Typed values for the options; an empty text means "not set" and is left out, a value that
    /// doesn't parse is reported instead of dropped.
    /// </summary>
    public bool TryBuild(IReadOnlyList<EntityOptionDescriptor> options, out Dictionary<string, object?> values, out string? error)
    {
        values = new(StringComparer.Ordinal);
        error = null;
        foreach (EntityOptionDescriptor option in options)
        {
            if (option.Type == EntityOptionType.Boolean)
            {
                values[option.Key] = Flags.GetValueOrDefault(option.Key);
                continue;
            }

            string text = Texts.GetValueOrDefault(option.Key)?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                continue;
            }

            switch (option.Type)
            {
                case EntityOptionType.Duration when TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out TimeSpan duration):
                    values[option.Key] = duration;
                    break;
                case EntityOptionType.Integer when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number):
                    values[option.Key] = number;
                    break;
                case EntityOptionType.Text or EntityOptionType.Choice:
                    values[option.Key] = text;
                    break;
                default:
                    error = $"{option.Label}: enter {(option.Type == EntityOptionType.Duration ? "a duration as hh:mm:ss (or d.hh:mm:ss)" : "a whole number")}.";
                    return false;
            }
        }

        return true;
    }

    /// <summary>A value as the form shows it (durations as hh:mm:ss).</summary>
    public static string Format(object? value) => value switch
    {
        null => string.Empty,
        bool flag => flag ? "yes" : "no",
        TimeSpan duration => duration.ToString("c", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
