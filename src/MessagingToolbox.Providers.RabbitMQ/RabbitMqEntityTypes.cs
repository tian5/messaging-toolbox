using System.Globalization;
using System.Text.Json;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Providers.RabbitMQ.Management;

namespace MessagingToolbox.Providers.RabbitMQ;

/// <summary>
/// What the management UI can create on RabbitMQ (ADR 21), and the mapping between those options
/// and management API declarations. RabbitMQ fixes a queue's or exchange's settings when it is
/// declared (a redeclaration with different ones is refused), so every option is create-only;
/// later changes go through policies. Option keys are the broker's own: the declaration fields
/// (<c>durable</c>, <c>auto-delete</c>, …) and otherwise the argument names.
/// </summary>
internal static class RabbitMqEntityTypes
{
    public const string QueueType = "x-queue-type";
    public const string ExchangeType = "type";
    public const string Durable = "durable";
    public const string AutoDelete = "auto-delete";
    public const string Internal = "internal";

    private const string FixedDescription = "RabbitMQ fixes these settings when the {0} is declared; change them later with a policy, or delete and recreate it.";

    private static readonly EntityTypeDescriptor Queue = new(EntityKind.Queue, "queue",
    [
        CreateOnly(new EntityOptionDescriptor(QueueType, "Type", EntityOptionType.Choice)
        {
            Choices = ["classic", "quorum", "stream"],
            DefaultValue = "classic",
            Description = "Quorum queues are replicated and track delivery counts; streams are append-only logs.",
        }),
        CreateOnly(new EntityOptionDescriptor(Durable, "Durable", EntityOptionType.Boolean)
        {
            DefaultValue = true,
            Description = "Survives a broker restart. Quorum queues and streams are always durable.",
        }),
        CreateOnly(new EntityOptionDescriptor(AutoDelete, "Auto-delete", EntityOptionType.Boolean) { DefaultValue = false }),
        CreateOnly(new EntityOptionDescriptor("x-message-ttl", "Message TTL", EntityOptionType.Duration) { Placeholder = "hh:mm:ss" }),
        CreateOnly(new EntityOptionDescriptor("x-expires", "Auto-expire when unused", EntityOptionType.Duration) { Placeholder = "hh:mm:ss" }),
        CreateOnly(new EntityOptionDescriptor("x-max-length", "Max length (messages)", EntityOptionType.Integer)),
        CreateOnly(new EntityOptionDescriptor("x-max-length-bytes", "Max length (bytes)", EntityOptionType.Integer)),
        CreateOnly(new EntityOptionDescriptor("x-overflow", "Overflow behaviour", EntityOptionType.Choice)
        {
            Choices = ["drop-head", "reject-publish", "reject-publish-dlx"],
        }),
        CreateOnly(new EntityOptionDescriptor("x-dead-letter-exchange", "Dead-letter exchange", EntityOptionType.Text)
        {
            Placeholder = "exchange name",
        }),
        CreateOnly(new EntityOptionDescriptor("x-dead-letter-routing-key", "Dead-letter routing key", EntityOptionType.Text)
        {
            Placeholder = "the message's own routing key",
        }),
        CreateOnly(new EntityOptionDescriptor("x-delivery-limit", "Delivery limit", EntityOptionType.Integer)
        {
            Description = "Quorum queues only: deliveries before a message is dead-lettered or dropped.",
        }),
        CreateOnly(new EntityOptionDescriptor("x-max-age", "Max age", EntityOptionType.Text)
        {
            Placeholder = "7D, 12h, 30m",
            Description = "Streams only: how long messages are retained.",
        }),
    ])
    {
        Description = string.Format(CultureInfo.InvariantCulture, FixedDescription, "queue"),
    };

    private static readonly EntityTypeDescriptor Exchange = new(EntityKind.Exchange, "exchange",
    [
        CreateOnly(new EntityOptionDescriptor(ExchangeType, "Type", EntityOptionType.Choice)
        {
            Choices = ["direct", "fanout", "topic", "headers"],
            DefaultValue = "direct",
        }),
        CreateOnly(new EntityOptionDescriptor(Durable, "Durable", EntityOptionType.Boolean) { DefaultValue = true }),
        CreateOnly(new EntityOptionDescriptor(AutoDelete, "Auto-delete", EntityOptionType.Boolean) { DefaultValue = false }),
        CreateOnly(new EntityOptionDescriptor(Internal, "Internal", EntityOptionType.Boolean)
        {
            DefaultValue = false,
            Description = "Only other exchanges can publish to it.",
        }),
        CreateOnly(new EntityOptionDescriptor("alternate-exchange", "Alternate exchange", EntityOptionType.Text)
        {
            Placeholder = "exchange name",
            Description = "Receives messages this exchange can't route.",
        }),
    ])
    {
        Description = string.Format(CultureInfo.InvariantCulture, FixedDescription, "exchange"),
    };

    public static readonly IReadOnlyList<EntityTypeDescriptor> All = [Queue, Exchange];

    // Options that are declaration fields rather than arguments.
    private static readonly HashSet<string> DeclarationFields = new(StringComparer.Ordinal) { ExchangeType, Durable, AutoDelete, Internal };

    public static RabbitMqQueueDeclaration ToQueueDeclaration(IReadOnlyDictionary<string, object?> options) => new(
        Flag(options, Durable, defaultValue: true),
        Flag(options, AutoDelete, defaultValue: false),
        ToArguments(options));

    public static RabbitMqExchangeDeclaration ToExchangeDeclaration(IReadOnlyDictionary<string, object?> options) => new(
        options.GetValueOrDefault(ExchangeType) as string is { Length: > 0 } type ? type : "direct",
        Flag(options, Durable, defaultValue: true),
        Flag(options, AutoDelete, defaultValue: false),
        Flag(options, Internal, defaultValue: false),
        ToArguments(options));

    public static IReadOnlyDictionary<string, object?> FromQueue(RabbitMqQueueInfo queue)
    {
        Dictionary<string, object?> options = FromArguments(Queue, queue.Arguments);
        options[QueueType] = queue.Type ?? "classic";
        options[Durable] = queue.Durable ?? true;
        options[AutoDelete] = queue.AutoDelete ?? false;
        return options;
    }

    public static IReadOnlyDictionary<string, object?> FromExchange(RabbitMqExchangeInfo exchange)
    {
        Dictionary<string, object?> options = FromArguments(Exchange, exchange.Arguments);
        options[ExchangeType] = exchange.Type ?? "direct";
        options[Durable] = exchange.Durable ?? true;
        options[AutoDelete] = exchange.AutoDelete ?? false;
        options[Internal] = exchange.Internal;
        return options;
    }

    private static EntityOptionDescriptor CreateOnly(EntityOptionDescriptor option) => option with { CreateOnly = true };

    private static bool Flag(IReadOnlyDictionary<string, object?> options, string key, bool defaultValue)
        => options.GetValueOrDefault(key) is bool value ? value : defaultValue;

    // Durations travel as TimeSpan and are milliseconds on the broker; empty values mean "not set".
    private static Dictionary<string, object> ToArguments(IReadOnlyDictionary<string, object?> options)
    {
        Dictionary<string, object> arguments = new(StringComparer.Ordinal);
        foreach ((string key, object? value) in options)
        {
            if (DeclarationFields.Contains(key))
            {
                continue;
            }

            object? argument = value switch
            {
                TimeSpan duration => (long)duration.TotalMilliseconds,
                string text when string.IsNullOrWhiteSpace(text) => null,
                string text => text.Trim(),
                long or int or bool => value,
                _ => null,
            };

            if (argument is not null)
            {
                arguments[key] = argument;
            }
        }

        return arguments;
    }

    private static Dictionary<string, object?> FromArguments(EntityTypeDescriptor type, Dictionary<string, JsonElement>? arguments)
    {
        Dictionary<string, object?> options = new(StringComparer.Ordinal);
        if (arguments is null)
        {
            return options;
        }

        foreach ((string key, JsonElement value) in arguments)
        {
            EntityOptionType? optionType = type.Options.FirstOrDefault(option => option.Key == key)?.Type;
            options[key] = (optionType, value.ValueKind) switch
            {
                (EntityOptionType.Duration, JsonValueKind.Number) when value.TryGetInt64(out long milliseconds) => TimeSpan.FromMilliseconds(milliseconds),
                (_, JsonValueKind.Number) when value.TryGetInt64(out long number) => number,
                (_, JsonValueKind.String) => value.GetString(),
                (_, JsonValueKind.True) => true,
                (_, JsonValueKind.False) => false,
                _ => value.GetRawText(),
            };
        }

        return options;
    }
}
