using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Aspire.Hosting.Tests;

public class MessagingToolboxExtensionsTests
{
    private static (IDistributedApplicationBuilder Builder, IResourceBuilder<ContainerResource> Host) CreateBuilderWithHost()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true });

        IResourceBuilder<ContainerResource> host = builder.AddResource(new ContainerResource("toolbox"));

        return (builder, host);
    }

    private static string Value(Dictionary<string, object> environment, string key)
        => environment[key] is ReferenceExpression expression
            ? expression.Format
            : environment[key].ToString() ?? string.Empty;

    private static async Task<Dictionary<string, object>> ResolveEnvironmentAsync(IResourceWithEnvironment host)
    {
        var environmentVariables = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run),
            host,
            environmentVariables,
            CancellationToken.None);

        foreach (EnvironmentCallbackAnnotation annotation in host.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback!(context);
        }

        return environmentVariables;
    }

    [Fact]
    public async Task WithMessagingToolboxConnection_TwoConnections_EmitsNonCollidingIndexes()
    {
        (IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource> host) = CreateBuilderWithHost();
        IResourceBuilder<AzureServiceBusResource> first = builder.AddAzureServiceBus("first");
        IResourceBuilder<AzureServiceBusResource> second = builder.AddAzureServiceBus("second");

        first.AddServiceBusQueue("orders");
        IResourceBuilder<AzureServiceBusTopicResource> billing = first.AddServiceBusTopic("billing-events");
        billing.AddServiceBusSubscription("processor");
        second.AddServiceBusQueue("shipments");


        host.WithMessagingToolboxConnection(first)
            .WithMessagingToolboxConnection(second);


        Dictionary<string, object> environment = await ResolveEnvironmentAsync(host.Resource);

        Value(environment, "ToolboxOptions__Connections__0__Name").ShouldBe("first");
        Value(environment, "ToolboxOptions__Connections__0__DisplayName").ShouldBe("first (Aspire)");
        Value(environment, "ToolboxOptions__Connections__0__Kind").ShouldBe("AzureServiceBus");
        Value(environment, "ToolboxOptions__Connections__0__Queues__0").ShouldBe("orders");
        Value(environment, "ToolboxOptions__Connections__0__Topics__0__Name").ShouldBe("billing-events");
        Value(environment, "ToolboxOptions__Connections__0__Topics__0__Subscriptions__0").ShouldBe("processor");
        Value(environment, "ToolboxOptions__Connections__1__Name").ShouldBe("second");
        Value(environment, "ToolboxOptions__Connections__1__DisplayName").ShouldBe("second (Aspire)");
        Value(environment, "ToolboxOptions__Connections__1__Kind").ShouldBe("AzureServiceBus");
        Value(environment, "ToolboxOptions__Connections__1__Queues__0").ShouldBe("shipments");
    }

    [Fact]
    public async Task WithMessagingToolboxConnection_SingleConnection_KeepsIndexZeroShape()
    {
        (IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource> host) = CreateBuilderWithHost();
        IResourceBuilder<AzureServiceBusResource> messaging = builder.AddAzureServiceBus("messaging");

        messaging.AddServiceBusQueue("orders");


        host.WithMessagingToolboxConnection(messaging);


        Dictionary<string, object> environment = await ResolveEnvironmentAsync(host.Resource);

        environment.Keys.ShouldNotContain(k => k.Contains("__Connections__1__", StringComparison.Ordinal));
        Value(environment, "ToolboxOptions__Connections__0__Name").ShouldBe("messaging");
        Value(environment, "ToolboxOptions__Connections__0__Queues__0").ShouldBe("orders");
    }

    [Fact]
    public async Task WithMessagingToolboxConnection_Emulator_PassesAdminEndpointReference()
    {
        (IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource> host) = CreateBuilderWithHost();
        IResourceBuilder<AzureServiceBusResource> messaging = builder.AddAzureServiceBus("messaging").RunAsEmulator();


        host.WithMessagingToolboxConnection(messaging);


        Dictionary<string, object> environment = await ResolveEnvironmentAsync(host.Resource);

        EndpointReference adminEndpoint = environment["ToolboxOptions__Connections__0__AdminEndpoint"].ShouldBeOfType<EndpointReference>();
        adminEndpoint.EndpointName.ShouldBe("emulatorhealth");
        adminEndpoint.Resource.ShouldBeSameAs(messaging.Resource);
    }

    [Fact]
    public async Task WithMessagingToolboxConnection_CloudNamespace_OmitsAdminEndpoint()
    {
        (IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource> host) = CreateBuilderWithHost();
        IResourceBuilder<AzureServiceBusResource> messaging = builder.AddAzureServiceBus("messaging");


        host.WithMessagingToolboxConnection(messaging);


        Dictionary<string, object> environment = await ResolveEnvironmentAsync(host.Resource);

        environment.Keys.ShouldNotContain("ToolboxOptions__Connections__0__AdminEndpoint");
    }

    [Fact]
    public void AddMessagingToolbox_Emulator_WaitsForBrokerChecksHealthAndPersistsData()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        IResourceBuilder<AzureServiceBusResource> messaging = builder.AddAzureServiceBus("messaging").RunAsEmulator();


        IResourceBuilder<MessagingToolboxResource> toolbox = builder.AddMessagingToolbox("toolbox", messaging);


        toolbox.Resource.Annotations.OfType<WaitAnnotation>().ShouldContain(wait => wait.Resource == messaging.Resource);
        toolbox.Resource.Annotations.OfType<HealthCheckAnnotation>().ShouldNotBeEmpty();
        ContainerMountAnnotation mount = toolbox.Resource.Annotations.OfType<ContainerMountAnnotation>().ShouldHaveSingleItem();
        mount.Target.ShouldBe("/app/data");
        mount.Type.ShouldBe(ContainerMountType.Volume);
    }

    [Fact]
    public void WithDataVolume_CustomName_ReplacesGeneratedVolume()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        IResourceBuilder<MessagingToolboxResource> toolbox = builder.AddMessagingToolbox("toolbox", builder.AddAzureServiceBus("messaging"));


        toolbox.WithDataVolume("my-toolbox-data");


        ContainerMountAnnotation mount = toolbox.Resource.Annotations.OfType<ContainerMountAnnotation>().ShouldHaveSingleItem();
        mount.Source.ShouldBe("my-toolbox-data");
        mount.Target.ShouldBe("/app/data");
    }

    [Fact]
    public async Task WithMessagingToolboxConnection_SameBrokerTwice_AddsOneConnection()
    {
        (IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource> host) = CreateBuilderWithHost();
        IResourceBuilder<AzureServiceBusResource> messaging = builder.AddAzureServiceBus("messaging");


        host.WithMessagingToolboxConnection(messaging)
            .WithMessagingToolboxConnection(messaging);


        Dictionary<string, object> environment = await ResolveEnvironmentAsync(host.Resource);

        Value(environment, "ToolboxOptions__Connections__0__Name").ShouldBe("messaging");
        environment.Keys.ShouldNotContain(k => k.Contains("__Connections__1__", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithMessagingToolbox_TwoBrokers_ShareOneToolbox()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        IResourceBuilder<AzureServiceBusResource> messaging = builder.AddAzureServiceBus("messaging").RunAsEmulator();
        IResourceBuilder<AzureServiceBusResource> audit = builder.AddAzureServiceBus("audit").RunAsEmulator();


        messaging.WithMessagingToolbox();
        audit.WithMessagingToolbox();


        MessagingToolboxResource toolbox = builder.Resources.OfType<MessagingToolboxResource>().ShouldHaveSingleItem();
        toolbox.Name.ShouldBe("messaging-toolbox");
        Dictionary<string, object> environment = await ResolveEnvironmentAsync(toolbox);
        Value(environment, "ToolboxOptions__Connections__0__Name").ShouldBe("messaging");
        Value(environment, "ToolboxOptions__Connections__1__Name").ShouldBe("audit");
    }

    [Fact]
    public void WithMessagingToolbox_PublishMode_AddsNoToolbox()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = ["--operation", "publish"], DisableDashboard = true });
        IResourceBuilder<AzureServiceBusResource> messaging = builder.AddAzureServiceBus("messaging");


        messaging.WithMessagingToolbox();


        builder.ExecutionContext.IsPublishMode.ShouldBeTrue();
        builder.Resources.OfType<MessagingToolboxResource>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WithMessagingToolboxConnection_RabbitMqWithManagementPlugin_EmitsKindAndManagementEndpoint()
    {
        (IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource> host) = CreateBuilderWithHost();
        IResourceBuilder<RabbitMQServerResource> rabbit = builder.AddRabbitMQ("rabbit").WithManagementPlugin();


        host.WithMessagingToolboxConnection(rabbit);


        Dictionary<string, object> environment = await ResolveEnvironmentAsync(host.Resource);

        Value(environment, "ToolboxOptions__Connections__0__Name").ShouldBe("rabbit");
        Value(environment, "ToolboxOptions__Connections__0__Kind").ShouldBe("RabbitMQ");
        EndpointReference management = environment["ToolboxOptions__Connections__0__AdminEndpoint"].ShouldBeOfType<EndpointReference>();
        management.EndpointName.ShouldBe("management");
        management.Resource.ShouldBeSameAs(rabbit.Resource);
    }

    [Fact]
    public async Task WithMessagingToolboxConnection_RabbitMqWithoutManagementPlugin_OmitsManagementEndpoint()
    {
        (IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource> host) = CreateBuilderWithHost();
        IResourceBuilder<RabbitMQServerResource> rabbit = builder.AddRabbitMQ("rabbit");


        host.WithMessagingToolboxConnection(rabbit);


        Dictionary<string, object> environment = await ResolveEnvironmentAsync(host.Resource);

        Value(environment, "ToolboxOptions__Connections__0__Kind").ShouldBe("RabbitMQ");
        environment.Keys.ShouldNotContain("ToolboxOptions__Connections__0__AdminEndpoint");
    }

    [Fact]
    public async Task WithMessagingToolbox_ServiceBusAndRabbitMq_ShareOneToolboxWithTheirKinds()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        IResourceBuilder<AzureServiceBusResource> messaging = builder.AddAzureServiceBus("messaging").RunAsEmulator();
        IResourceBuilder<RabbitMQServerResource> rabbit = builder.AddRabbitMQ("rabbit").WithManagementPlugin();


        messaging.WithMessagingToolbox();
        rabbit.WithMessagingToolbox();


        MessagingToolboxResource toolbox = builder.Resources.OfType<MessagingToolboxResource>().ShouldHaveSingleItem();
        toolbox.Annotations.OfType<WaitAnnotation>().ShouldContain(wait => wait.Resource == rabbit.Resource);
        Dictionary<string, object> environment = await ResolveEnvironmentAsync(toolbox);
        Value(environment, "ToolboxOptions__Connections__0__Kind").ShouldBe("AzureServiceBus");
        Value(environment, "ToolboxOptions__Connections__1__Name").ShouldBe("rabbit");
        Value(environment, "ToolboxOptions__Connections__1__Kind").ShouldBe("RabbitMQ");
    }
}
