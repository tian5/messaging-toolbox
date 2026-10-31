using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace MessagingToolbox.Aspire.Hosting.Brokers;

/// <summary>
/// What the toolbox needs to know about one kind of broker resource: the provider kind it maps
/// to, and the broker-specific connection configuration (management endpoint, modeled entities).
/// </summary>
internal interface IMessagingToolboxBroker
{
    /// <summary>The toolbox's <c>ProviderKind</c> name for this broker.</summary>
    string Kind { get; }

    /// <summary>
    /// Adds the broker-specific values under <paramref name="prefix"/>
    /// (<c>ToolboxOptions__Connections__{i}</c>). Runs when environment values are computed, so the
    /// application model is complete.
    /// </summary>
    void AddConnectionEnvironment(
        EnvironmentCallbackContext context,
        string prefix,
        IResource broker,
        IDistributedApplicationBuilder applicationBuilder);
}
