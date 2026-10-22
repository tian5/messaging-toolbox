using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Web.Services.Helpers;

/// <summary>Display names and connection-string hints for the broker kinds offered in the connection editor.</summary>
public static class ProviderKindLabels
{
    public static string LabelOf(ProviderKind kind) => kind switch
    {
        ProviderKind.AzureServiceBus => "Azure Service Bus",
        _ => kind.ToString(),
    };

    public static string ConnectionStringPlaceholderOf(ProviderKind kind) => kind switch
    {
        ProviderKind.AzureServiceBus => "Endpoint=sb://…",
        _ => string.Empty,
    };
}
