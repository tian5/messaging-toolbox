using Aspire.Hosting.ApplicationModel;

namespace MessagingToolbox.Aspire.Hosting;

/// <summary>
/// The MessagingToolbox container resource: the explorer UI for broker connections, served from
/// the ghcr.io image and wired to the referenced broker by the hosting extensions.
/// </summary>
public class MessagingToolboxResource(string name) : ContainerResource(name)
{
    internal const string HttpEndpointName = "http";

    private EndpointReference? _httpEndpoint;

    /// <summary>The HTTP endpoint of the toolbox web UI, shown on the Aspire dashboard.</summary>
    public EndpointReference HttpEndpoint => _httpEndpoint ??= new EndpointReference(this, HttpEndpointName);
}
