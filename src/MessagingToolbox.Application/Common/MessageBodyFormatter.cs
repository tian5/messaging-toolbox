using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace MessagingToolbox.Application.Common;

/// <summary>Indents JSON and XML bodies for the detail view; anything else is returned as is.</summary>
public static class MessageBodyFormatter
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,

        // Display only (the UI encodes the output): keep non-ASCII text and markup characters readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly XmlReaderSettings _xmlSettings = new()
    {
        // Bodies are untrusted broker input — no DTDs, no external resolution.
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    /// <summary>
    /// The body indented for reading. A body that doesn't parse as the given format comes back
    /// unchanged, so the view never loses content.
    /// </summary>
    public static string Pretty(string body, MessageBodyFormat format)
    {
        try
        {
            return format switch
            {
                MessageBodyFormat.Json => PrettyJson(body),
                MessageBodyFormat.Xml => PrettyXml(body),
                _ => body,
            };
        }
        catch (Exception exception) when (exception is JsonException or XmlException)
        {
            return body;
        }
    }

    private static string PrettyJson(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        return JsonSerializer.Serialize(document.RootElement, _jsonOptions);
    }

    private static string PrettyXml(string body)
    {
        using StringReader reader = new(body);
        using XmlReader xmlReader = XmlReader.Create(reader, _xmlSettings);
        XDocument document = XDocument.Load(xmlReader);
        string declaration = document.Declaration is null ? string.Empty : document.Declaration + Environment.NewLine;
        return declaration + document.ToString();
    }
}
