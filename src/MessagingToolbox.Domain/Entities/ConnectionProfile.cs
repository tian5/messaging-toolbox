using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Domain.Entities;

/// <summary>A saved broker connection (FR-1.1). Aspire-seeded connections come from configuration instead.</summary>
public class ConnectionProfile : EntityBase
{
    public string DisplayName { get; set; } = string.Empty;

    public ProviderKind Kind { get; set; } = ProviderKind.AzureServiceBus;

    /// <summary>
    /// Connection string protected with ASP.NET Core Data Protection (Phase 1, NFR-4);
    /// never logged and masked in the UI.
    /// </summary>
    public string ProtectedConnectionString { get; set; } = string.Empty;
}
