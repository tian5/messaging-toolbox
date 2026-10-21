using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Configuration;

/// <summary>A connection seeded from configuration.</summary>
public sealed class PreconfiguredConnectionOptions
{
    /// <summary>Logical name; also used to resolve the connection string from ConnectionStrings:{Name}.</summary>
    public string Name { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    /// <summary>Optional inline connection string; falls back to ConnectionStrings:{Name}.</summary>
    public string? ConnectionString { get; set; }

    public ProviderKind Kind { get; set; } = ProviderKind.AzureServiceBus;

    /// <summary>
    /// Management endpoint when it can't be derived from the connection string — the Azure Service
    /// Bus emulator's admin port (5300) as resolved by the AppHost, e.g. <c>http://localhost:51234</c>.
    /// </summary>
    public string? AdminEndpoint { get; set; }

    /// <summary>Queue names modeled by the host (Aspire AppHost), feeding emulator entity discovery.</summary>
    public List<string> Queues { get; set; } = [];

    /// <summary>Topics (with subscriptions) modeled by the host, feeding emulator entity discovery.</summary>
    public List<ModeledTopicOptions> Topics { get; set; } = [];
}
