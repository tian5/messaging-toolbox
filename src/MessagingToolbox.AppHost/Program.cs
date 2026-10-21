using Aspire.Hosting.Azure;
using MessagingToolbox.Aspire.Hosting;
using Projects;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<AzureServiceBusResource> messaging = builder.AddAzureServiceBus("messaging")
    .RunAsEmulator(emulator => emulator
        .WithImageTag("2.0.1"));

messaging.AddServiceBusQueue("orders");
messaging.AddServiceBusQueue("billing-requests");

IResourceBuilder<AzureServiceBusTopicResource> billing = messaging.AddServiceBusTopic("billing-events");
billing.AddServiceBusSubscription("processor");
billing.AddServiceBusSubscription("processor2");

// A second namespace demonstrates the multi-connection story: each WithMessagingToolboxConnection
// call composes independently and the toolbox shows both connections side by side.
IResourceBuilder<AzureServiceBusResource> audit = builder.AddAzureServiceBus("audit")
    .RunAsEmulator(emulator => emulator
        .WithImageTag("2.0.1"));

audit.AddServiceBusQueue("audit-log");
IResourceBuilder<AzureServiceBusTopicResource> auditEvents = audit.AddServiceBusTopic("audit-events");
auditEvents.AddServiceBusSubscription("archiver");

if (bool.TryParse(builder.Configuration["Toolbox:RunFromSource"], out bool runFromSource) && runFromSource)
{
    builder.AddProject<MessagingToolbox_Web>("toolbox")
        .WithMessagingToolboxConnection(messaging)
        .WithMessagingToolboxConnection(audit);
}
else
{
    builder.AddMessagingToolbox("toolbox", messaging)
        .WithMessagingToolboxConnection(audit);
}

builder.Build().Run();
