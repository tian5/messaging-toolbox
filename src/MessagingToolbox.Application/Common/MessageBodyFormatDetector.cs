using System.Text.Json;
using System.Xml;

namespace MessagingToolbox.Application.Common;

/// <summary>Lightweight body-format detection for the detail view.</summary>
public static class MessageBodyFormatDetector
{
    private static readonly XmlReaderSettings _xmlDetectionSettings = new()
    {
        // These match the secure defaults, but bodies are untrusted broker input — be explicit.
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreWhitespace = true,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    public static MessageBodyFormat Detect(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return MessageBodyFormat.Text;
        }

        return body.AsSpan().TrimStart()[0] switch
        {
            '<' => IsWellFormedXml(body) ? MessageBodyFormat.Xml : MessageBodyFormat.Text,
            '{' or '[' => IsJsonDocument(body) ? MessageBodyFormat.Json : MessageBodyFormat.Text,
            _ => MessageBodyFormat.Text,
        };
    }

    private static bool IsJsonDocument(string value)
    {
        try
        {
            // Only objects and arrays count — scalars like 123, true, or "x" read as plain text to the user.
            using JsonDocument document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsWellFormedXml(string value)
    {
        try
        {
            using StringReader reader = new(value);
            using XmlReader xmlReader = XmlReader.Create(reader, _xmlDetectionSettings);

            while (xmlReader.Read())
            {
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }
}
