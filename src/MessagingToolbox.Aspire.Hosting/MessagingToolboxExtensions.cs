using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Utils;

namespace MessagingToolbox.Aspire.Hosting;

/// <summary>AppHost extensions for wiring MessagingToolbox to a broker resource.</summary>
public static class MessagingToolboxExtensions
{
    private const int ToolboxHttpContainerPort = 8080;

    private const string DefaultResourceName = "messaging-toolbox";

    private const string HealthEndpointPath = "/health";

    // Writable state directory of the image (Dockerfile; ToolboxOptions:DataDirectory).
    private const string DataDirectory = "/app/data";

    // Endpoint name Aspire's RunAsEmulator gives the emulator's port 5300 (health + admin API).
    private const string EmulatorAdminEndpointName = "emulatorhealth";

    /// <summary>
    /// Adds the MessagingToolbox container and connects it to <paramref name="serviceBus"/> —
    /// works against cloud namespaces and <c>.RunAsEmulator()</c>. Entities modeled in the
    /// AppHost (AddServiceBusQueue/Topic/Subscription) are injected as configuration, the
    /// preferred emulator discovery channel (architecture doc §8). The container starts once the
    /// broker is healthy, reports its own health on <c>/health</c>, and keeps its state (saved
    /// connections, canned messages, audit log, Data Protection keys) in a named volume.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Resource name, also shown on the dashboard.</param>
    /// <param name="serviceBus">The broker resource to explore (connection info flows in via environment variables).</param>
    public static IResourceBuilder<MessagingToolboxResource> AddMessagingToolbox(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        IResourceBuilder<IResourceWithConnectionString> serviceBus)
    {
        MessagingToolboxResource resource = new(name);

        IResourceBuilder<MessagingToolboxResource> toolbox = builder.AddResource(resource)
            .WithImage(MessagingToolboxContainerImageTags.Image, MessagingToolboxContainerImageTags.Tag)
            .WithImageRegistry(MessagingToolboxContainerImageTags.Registry)
            .WithHttpEndpoint(targetPort: ToolboxHttpContainerPort, name: MessagingToolboxResource.HttpEndpointName)
            .WithHttpHealthCheck(HealthEndpointPath, endpointName: MessagingToolboxResource.HttpEndpointName)
            .WithDataVolume();

        return toolbox.WithMessagingToolboxConnection(serviceBus);
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
    {
        IDistributedApplicationBuilder applicationBuilder = serviceBus.ApplicationBuilder;
        if (applicationBuilder.ExecutionContext.IsPublishMode)
        {
            return serviceBus;
        }

        MessagingToolboxResource? existing = applicationBuilder.Resources.OfType<MessagingToolboxResource>().FirstOrDefault();
        if (existing is not null)
        {
            applicationBuilder.CreateResourceBuilder(existing).WithMessagingToolboxConnection(serviceBus);
            return serviceBus;
        }

        IResourceBuilder<MessagingToolboxResource> toolbox =
            applicationBuilder.AddMessagingToolbox(containerName ?? DefaultResourceName, serviceBus);
        configureContainer?.Invoke(toolbox);

        return serviceBus;
    }

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
    /// Connects any MessagingToolbox host to <paramref name="serviceBus"/>: the connection string
    /// flows in as ConnectionStrings__{name} and the AppHost-modeled entities as ToolboxOptions
    /// configuration. Apply this to a project resource to run MessagingToolbox.Web from source
    /// (e.g. for debugging) with wiring identical to the published container.
    /// </summary>
    public static IResourceBuilder<TResource> WithMessagingToolboxConnection<TResource>(
        this IResourceBuilder<TResource> builder,
        IResourceBuilder<IResourceWithConnectionString> serviceBus)
        where TResource : IResourceWithEnvironment
    {
        IDistributedApplicationBuilder applicationBuilder = builder.ApplicationBuilder;
        string connectionName = serviceBus.Resource.Name;

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
        if (!connections.AttachedBrokers.Add(serviceBus.Resource))
        {
            return builder;
        }

        int index = Interlocked.Increment(ref connections.NextIndex) - 1;

        // Start the toolbox once the broker is healthy (the emulator needs its SQL companion
        // first); hosts that can't wait (plain environment resources) start right away.
        if (builder is IResourceBuilder<IResourceWithWaitSupport> waitingHost)
        {
            waitingHost.WaitFor(serviceBus);
        }

        builder.WithReference(serviceBus)
            .WithEnvironment($"ToolboxOptions__Connections__{index}__Name", connectionName)
            .WithEnvironment($"ToolboxOptions__Connections__{index}__DisplayName", $"{connectionName} (Aspire)")
            // Provider kind understood by the toolbox; other brokers get their own kind as providers land.
            .WithEnvironment($"ToolboxOptions__Connections__{index}__Kind", "AzureServiceBus");

        // Resolved when environment values are computed (never at wiring time — the model is
        // complete then, so queues/topics added after this call are still discovered).
        builder.WithEnvironment(context =>
        {
            // The emulator's admin API listens on container port 5300, which Aspire maps to a
            // dynamic host port — the toolbox can't derive it from the connection string when it
            // runs from source. The endpoint reference resolves for the consumer (host or container).
            if (serviceBus.Resource is AzureServiceBusResource { IsEmulator: true } emulator)
            {
                context.EnvironmentVariables[$"ToolboxOptions__Connections__{index}__AdminEndpoint"] =
                    new EndpointReference(emulator, EmulatorAdminEndpointName);
            }

            int queueIndex = 0;
            foreach (AzureServiceBusQueueResource queue in applicationBuilder.Resources.OfType<AzureServiceBusQueueResource>())
            {
                if (!ReferenceEquals(queue.Parent, serviceBus.Resource))
                {
                    continue;
                }

                context.EnvironmentVariables[$"ToolboxOptions__Connections__{index}__Queues__{queueIndex++}"] = queue.QueueName;
            }

            int topicIndex = 0;
            foreach (AzureServiceBusTopicResource topic in applicationBuilder.Resources.OfType<AzureServiceBusTopicResource>())
            {
                if (!ReferenceEquals(topic.Parent, serviceBus.Resource))
                {
                    continue;
                }

                string topicPrefix = $"ToolboxOptions__Connections__{index}__Topics__{topicIndex++}";
                context.EnvironmentVariables[$"{topicPrefix}__Name"] = topic.TopicName;

                int subscriptionIndex = 0;
                foreach (AzureServiceBusSubscriptionResource subscription in applicationBuilder.Resources.OfType<AzureServiceBusSubscriptionResource>())
                {
                    if (!ReferenceEquals(subscription.Parent, topic))
                    {
                        continue;
                    }

                    context.EnvironmentVariables[$"{topicPrefix}__Subscriptions__{subscriptionIndex++}"] = subscription.SubscriptionName;
                }
            }
        });

        return builder;
    }
}
