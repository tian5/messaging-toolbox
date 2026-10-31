using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Web.Services.Helpers;

/// <summary>Display names and connection-string hints for the broker kinds offered in the connection editor.</summary>
public static class ProviderKindLabels
{
    public static string LabelOf(ProviderKind kind) => kind switch
    {
        ProviderKind.AzureServiceBus => "Azure Service Bus",
        ProviderKind.RabbitMQ => "RabbitMQ",
        _ => kind.ToString(),
    };

    public static string ConnectionStringPlaceholderOf(ProviderKind kind) => kind switch
    {
        ProviderKind.AzureServiceBus => "Endpoint=sb://…",
        ProviderKind.RabbitMQ => "amqp://guest:guest@localhost:5672/  (append ;Management=http://host:15672 if not on :15672)",
        _ => string.Empty,
    };
}
