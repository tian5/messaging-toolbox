using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Utils;
using MessagingToolbox.Aspire.Hosting.Brokers;

namespace MessagingToolbox.Aspire.Hosting;

/// <summary>AppHost extensions for wiring MessagingToolbox to a broker resource.</summary>
public static class MessagingToolboxExtensions
{
    private const int ToolboxHttpContainerPort = 8080;

    private const string DefaultResourceName = "messaging-toolbox";

    private const string HealthEndpointPath = "/health";

    // Writable state directory of the image (Dockerfile; ToolboxOptions:DataDirectory).
    private const string DataDirectory = "/app/data";

    /// <summary>
    /// Adds the MessagingToolbox container and connects it to <paramref name="broker"/>: an Azure
    /// Service Bus namespace (cloud or <c>.RunAsEmulator()</c>) or a RabbitMQ server. Entities
    /// modeled in the AppHost (AddServiceBusQueue/Topic/Subscription) and the RabbitMQ management
    /// endpoint (<c>WithManagementPlugin()</c>) are injected as configuration (architecture doc §8).
    /// The container starts once the broker is healthy, reports its own health on <c>/health</c>,
    /// and keeps its state (saved connections, canned messages, audit log, Data Protection keys)
    /// in a named volume.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Resource name, also shown on the dashboard.</param>
    /// <param name="broker">The broker resource to explore (connection info flows in via environment variables).</param>
    public static IResourceBuilder<MessagingToolboxResource> AddMessagingToolbox(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        IResourceBuilder<IResourceWithConnectionString> broker)
    {
        MessagingToolboxResource resource = new(name);

        IResourceBuilder<MessagingToolboxResource> toolbox = builder.AddResource(resource)
            .WithImage(MessagingToolboxContainerImageTags.Image, MessagingToolboxContainerImageTags.Tag)
            .WithImageRegistry(MessagingToolboxContainerImageTags.Registry)
            .WithHttpEndpoint(targetPort: ToolboxHttpContainerPort, name: MessagingToolboxResource.HttpEndpointName)
            .WithHttpHealthCheck(HealthEndpointPath, endpointName: MessagingToolboxResource.HttpEndpointName)
            .WithDataVolume();

        return toolbox.WithMessagingToolboxConnection(broker);
    }

    /// <summary>
    /// Adds a MessagingToolbox container for this broker — or, when the AppHost already has one,
    /// attaches this broker to it, so several namespaces share one toolbox. Nothing is added when
    /// publishing: the toolbox is a development tool.
    /// </summary>
    /// <param name="serviceBus">The broker to explore.</param>
    /// <param name="configureContainer">Applied when the toolbox container is created by this call.</param>
    /// <param name="containerName">Name of the toolbox resource; defaults to <c>messaging-toolbox</c>.</param>
    public static IResourceBuilder<AzureServiceBusResource> WithMessagingToolbox(
        this IResourceBuilder<AzureServiceBusResource> serviceBus,
        Action<IResourceBuilder<MessagingToolboxResource>>? configureContainer = null,
        string? containerName = null)
        => AttachToToolbox(serviceBus, configureContainer, containerName);

    /// <summary>
    /// Adds a MessagingToolbox container for this RabbitMQ server — or, when the AppHost already has
    /// one, attaches this broker to it. Call <c>WithManagementPlugin()</c> on the server so the toolbox
    /// can discover queues and exchanges. Nothing is added when publishing: the toolbox is a
    /// development tool.
    /// </summary>
    /// <param name="rabbitMq">The broker to explore.</param>
    /// <param name="configureContainer">Applied when the toolbox container is created by this call.</param>
    /// <param name="containerName">Name of the toolbox resource; defaults to <c>messaging-toolbox</c>.</param>
    public static IResourceBuilder<RabbitMQServerResource> WithMessagingToolbox(
        this IResourceBuilder<RabbitMQServerResource> rabbitMq,
        Action<IResourceBuilder<MessagingToolboxResource>>? configureContainer = null,
        string? containerName = null)
        => AttachToToolbox(rabbitMq, configureContainer, containerName);

    /// <summary>
    /// Mounts a named volume on the toolbox's data directory (<c>/app/data</c>). Without it, a
    /// recreated container loses its SQLite state and its Data Protection keys — saved connection
    /// strings then can no longer be decrypted. <see cref="AddMessagingToolbox"/> calls this with a
    /// generated name; call it again to choose the name.
    /// </summary>
    public static IResourceBuilder<MessagingToolboxResource> WithDataVolume(
        this IResourceBuilder<MessagingToolboxResource> builder,
        string? name = null)
    {
        foreach (ContainerMountAnnotation mount in builder.Resource.Annotations.OfType<ContainerMountAnnotation>()
            .Where(mount => mount.Target == DataDirectory)
            .ToList())
        {
            builder.Resource.Annotations.Remove(mount);
        }

        return builder.WithVolume(name ?? VolumeNameGenerator.Generate(builder, "data"), DataDirectory);
    }

    /// <summary>
    /// Connects any MessagingToolbox host to <paramref name="broker"/>: the connection string
    /// flows in as ConnectionStrings__{name}, the broker kind and its AppHost-modeled details
    /// (entities, management endpoint) as ToolboxOptions configuration. Apply this to a project
    /// resource to run MessagingToolbox.Web from source (e.g. for debugging) with wiring identical
    /// to the published container.
    /// </summary>
    public static IResourceBuilder<TResource> WithMessagingToolboxConnection<TResource>(
        this IResourceBuilder<TResource> builder,
        IResourceBuilder<IResourceWithConnectionString> broker)
        where TResource : IResourceWithEnvironment
    {
        IDistributedApplicationBuilder applicationBuilder = builder.ApplicationBuilder;
        string connectionName = broker.Resource.Name;
        IMessagingToolboxBroker brokerKind = MessagingToolboxBrokers.For(broker.Resource);

        // The connection string itself arrives via WithReference as ConnectionStrings__{name};
        // these values tell the toolbox the connection exists and what the AppHost modeled.
        // Each call gets its own connection index, so several brokers can be attached to one host.
        MessagingToolboxConnectionsAnnotation connections = builder.Resource.Annotations
            .OfType<MessagingToolboxConnectionsAnnotation>()
            .FirstOrDefault() ?? new MessagingToolboxConnectionsAnnotation();
        if (!builder.Resource.Annotations.Contains(connections))
        {
            builder.WithAnnotation(connections);
        }

        // The same broker attached twice would yield two connections with one name (and one id).
        if (!connections.AttachedBrokers.Add(broker.Resource))
        {
            return builder;
        }

        int index = Interlocked.Increment(ref connections.NextIndex) - 1;
        string prefix = $"ToolboxOptions__Connections__{index}";

        // Start the toolbox once the broker is healthy (the emulator needs its SQL companion
        // first); hosts that can't wait (plain environment resources) start right away.
        if (builder is IResourceBuilder<IResourceWithWaitSupport> waitingHost)
        {
            waitingHost.WaitFor(broker);
        }

        builder.WithReference(broker)
            .WithEnvironment($"{prefix}__Name", connectionName)
            .WithEnvironment($"{prefix}__DisplayName", $"{connectionName} (Aspire)")
            .WithEnvironment($"{prefix}__Kind", brokerKind.Kind);

        // Resolved when environment values are computed (never at wiring time — the model is
        // complete then, so entities added after this call are still discovered).
        builder.WithEnvironment(context => brokerKind.AddConnectionEnvironment(context, prefix, broker.Resource, applicationBuilder));

        return builder;
    }

    private static IResourceBuilder<TBroker> AttachToToolbox<TBroker>(
        IResourceBuilder<TBroker> broker,
        Action<IResourceBuilder<MessagingToolboxResource>>? configureContainer,
        string? containerName)
        where TBroker : IResourceWithConnectionString
    {
        IDistributedApplicationBuilder applicationBuilder = broker.ApplicationBuilder;
        if (applicationBuilder.ExecutionContext.IsPublishMode)
        {
            return broker;
        }

        IResourceBuilder<IResourceWithConnectionString> connection = applicationBuilder.CreateResourceBuilder<IResourceWithConnectionString>(broker.Resource);
        MessagingToolboxResource? existing = applicationBuilder.Resources.OfType<MessagingToolboxResource>().FirstOrDefault();
        if (existing is not null)
        {
            applicationBuilder.CreateResourceBuilder(existing).WithMessagingToolboxConnection(connection);
            return broker;
        }

        IResourceBuilder<MessagingToolboxResource> toolbox =
            applicationBuilder.AddMessagingToolbox(containerName ?? DefaultResourceName, connection);
        configureContainer?.Invoke(toolbox);

        return broker;
    }
}
