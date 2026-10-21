using System.Text.Json;
using System.Text.Json.Serialization;

namespace MessagingToolbox.Application.Common;

/// <summary>
/// The stored form of a canned message's application properties. Text properties stay plain
/// strings (<c>{"key": "value"}</c>, the original format); typed ones are stored as
/// <c>{"key": {"value": "42", "type": "Int32"}}</c>. Both forms read back, so existing databases
/// need no migration.
/// </summary>
public static class CannedPropertiesJson
{
    public static string Serialize(IReadOnlyDictionary<string, string> properties, IReadOnlyDictionary<string, PropertyValueType>? types)
    {
        Dictionary<string, object> stored = properties.ToDictionary(
            pair => pair.Key,
            pair => types?.GetValueOrDefault(pair.Key) is { } type and not PropertyValueType.String
                ? (object)new TypedValue(pair.Value, type.ToString())
                : pair.Value);
        return JsonSerializer.Serialize(stored);
    }

    public static (Dictionary<string, string> Properties, Dictionary<string, PropertyValueType> Types) Deserialize(string json)
    {
        Dictionary<string, string> properties = [];
        Dictionary<string, PropertyValueType> types = [];
        Dictionary<string, JsonElement> stored = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
        foreach ((string key, JsonElement element) in stored)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("value", out JsonElement value)
                && element.TryGetProperty("type", out JsonElement type)
                && Enum.TryParse(type.GetString(), out PropertyValueType parsedType))
            {
                properties[key] = value.GetString() ?? string.Empty;
                types[key] = parsedType;
            }
            else
            {
                properties[key] = element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.ToString();
            }
        }

        return (properties, types);
    }

    private sealed record TypedValue(
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("type")] string Type);
}
