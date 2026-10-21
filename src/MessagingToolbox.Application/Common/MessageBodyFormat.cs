using System.Text.Json.Serialization;

namespace MessagingToolbox.Application.Common;

/// <summary>Detected body format for display and the composer editor.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MessageBodyFormat
{
    [JsonStringEnumMemberName("json")]
    Json,

    [JsonStringEnumMemberName("xml")]
    Xml,

    [JsonStringEnumMemberName("text")]
    Text,
}
