using System.Collections;
using System.Globalization;
using System.Text;
using MessagingToolbox.Abstractions.Messaging;
using RabbitMQ.Client;

namespace MessagingToolbox.Providers.RabbitMQ;

/// <summary>
/// AMQP 0-9-1 message ↔ <see cref="ToolboxMessage"/>. Headers the broker sets (death history,
/// delivery counts, stream offsets) go to <see cref="ToolboxMessage.BrokerProperties"/>, so a
/// resubmitted clone never carries them; every other header is an application property.
/// </summary>
internal static class RabbitMqMessageMapper
{
    public const string ExchangeProperty = "exchange";
    public const string RedeliveredProperty = "redelivered";
    public const string DeliveryCountHeader = "x-delivery-count";
    public const string StreamOffsetHeader = "x-stream-offset";

    private const string FirstDeathReasonHeader = "x-first-death-reason";
    private const string FirstDeathQueueHeader = "x-first-death-queue";
    private const string FirstDeathExchangeHeader = "x-first-death-exchange";

    private static readonly string[] BrokerHeaderPrefixes =
    [
        "x-death", "x-first-death-", "x-last-death-", DeliveryCountHeader, "x-acquired-count", "x-stream-",
    ];

    /// <summary>
    /// RabbitMQ has no sequence numbers; stream messages report their offset as one, every other
    /// message none (the grid keys rows by load position instead).
    /// </summary>
    public static ToolboxMessage ToToolboxMessage(
        IReadOnlyBasicProperties properties,
        ReadOnlyMemory<byte> body,
        string exchange,
        string routingKey,
        bool redelivered)
    {
        Dictionary<string, object?> applicationProperties = new(StringComparer.Ordinal);
        Dictionary<string, object?> brokerProperties = new(StringComparer.Ordinal)
        {
            [ExchangeProperty] = exchange.Length == 0 ? "(default)" : exchange,
            [RedeliveredProperty] = redelivered,
        };

        if (properties.Headers is { } headers)
        {
            foreach (KeyValuePair<string, object?> header in headers)
            {
                bool isBrokerHeader = BrokerHeaderPrefixes.Any(prefix => header.Key.StartsWith(prefix, StringComparison.Ordinal));
                (isBrokerHeader ? brokerProperties : applicationProperties)[header.Key] = isBrokerHeader
                    ? FormatForDisplay(ToClrValue(header.Value))
                    : ToClrValue(header.Value);
            }
        }

        long? streamOffset = AsLong(Header(StreamOffsetHeader));

        return new ToolboxMessage
        {
            Body = body.ToArray(),
            MessageId = properties.MessageId,
            CorrelationId = properties.CorrelationId,
            ContentType = properties.ContentType,
            ReplyTo = properties.ReplyTo,
            Subject = properties.Type,
            RoutingKey = routingKey,
            TimeToLive = long.TryParse(properties.Expiration, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ttlMs)
                ? TimeSpan.FromMilliseconds(ttlMs)
                : null,
            EnqueuedTime = properties.IsTimestampPresent()
                ? DateTimeOffset.FromUnixTimeSeconds(properties.Timestamp.UnixTime)
                : null,
            SequenceNumber = streamOffset,
            State = MessageState.Active,
            DeliveryCount = AsLong(Header(DeliveryCountHeader)) is { } deliveryCount
                ? (int)Math.Min(deliveryCount, int.MaxValue)
                : redelivered ? 1 : 0,
            DeadLetterReason = Header(FirstDeathReasonHeader) as string,
            DeadLetterErrorDescription = DeathOrigin(Header(FirstDeathQueueHeader) as string, Header(FirstDeathExchangeHeader) as string),
            DeadLetterSourcePath = Header(FirstDeathQueueHeader) is string sourceQueue ? RabbitMqEntityPath.ForQueue(sourceQueue) : null,
            ApplicationProperties = applicationProperties,
            BrokerProperties = brokerProperties,
        };

        object? Header(string key) => properties.Headers?.TryGetValue(key, out object? value) == true ? ToClrValue(value) : null;
    }

    /// <summary>Properties for publishing; messages are persistent and stamped with the send time.</summary>
    public static BasicProperties ToBasicProperties(ToolboxMessage message, DateTimeOffset now)
    {
        BasicProperties properties = new()
        {
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(now.ToUnixTimeSeconds()),
            MessageId = message.MessageId,
            CorrelationId = message.CorrelationId,
            ContentType = message.ContentType,
            ReplyTo = message.ReplyTo,
            Type = message.Subject,
        };

        if (message.TimeToLive is { } ttl)
        {
            properties.Expiration = ((long)ttl.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
        }

        if (message.ApplicationProperties.Count > 0)
        {
            properties.Headers = message.ApplicationProperties.ToDictionary(
                property => property.Key,
                property => ToAmqpValue(property.Value),
                StringComparer.Ordinal);
        }

        return properties;
    }

    // AMQP field values: long strings arrive as byte[], tables as dictionaries, arrays as lists.
    internal static object? ToClrValue(object? value) => value switch
    {
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        AmqpTimestamp timestamp => DateTimeOffset.FromUnixTimeSeconds(timestamp.UnixTime),
        IDictionary<string, object?> table => table.ToDictionary(entry => entry.Key, entry => ToClrValue(entry.Value), StringComparer.Ordinal),
        IList list => list.Cast<object?>().Select(ToClrValue).ToList(),
        _ => value,
    };

    // Brokers encode numeric headers with whichever AMQP integer type fits.
    private static long? AsLong(object? value) => value switch
    {
        long number => number,
        int number => number,
        short number => number,
        byte number => number,
        _ => null,
    };

    private static object? ToAmqpValue(object? value) => value switch
    {
        null or string or bool or byte or sbyte or short or int or long or float or double or decimal => value,
        DateTimeOffset timestamp => new AmqpTimestamp(timestamp.ToUnixTimeSeconds()),
        DateTime dateTime => new AmqpTimestamp(new DateTimeOffset(dateTime.ToUniversalTime()).ToUnixTimeSeconds()),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    // x-death is a list of tables; render it readably for the detail view.
    private static object? FormatForDisplay(object? value) => value switch
    {
        IDictionary<string, object?> table => "{" + string.Join(", ", table.Select(entry => $"{entry.Key}={FormatForDisplay(entry.Value)}")) + "}",
        IList list => "[" + string.Join(", ", list.Cast<object?>().Select(FormatForDisplay)) + "]",
        DateTimeOffset timestamp => timestamp.ToString("u", CultureInfo.InvariantCulture),
        _ => value,
    };

    private static string? DeathOrigin(string? queue, string? exchange) => queue is null
        ? null
        : exchange is null or "" ? $"Dead-lettered from queue '{queue}'." : $"Dead-lettered from queue '{queue}' (published via exchange '{exchange}').";
}
