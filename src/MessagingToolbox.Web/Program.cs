using MessagingToolbox.Application;
using MessagingToolbox.Application.Behaviors;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Infrastructure;
using MessagingToolbox.Providers.AzureServiceBus;
using MessagingToolbox.Web.Components;
using MessagingToolbox.Web.Services;
using MessagingToolbox.Web.Services.Session;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
ConfigureServices(builder.Services, builder.Configuration);

WebApplication app = builder.Build();

ConfigurePipeline(app);

app.Run();

static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    services.AddRazorComponents()
        .AddInteractiveServerComponents();

    services.AddScoped<PollingRefreshService>();
    services.AddScoped<LiveWatchService>();
    services.AddScoped<NotificationService>();
    services.AddScoped<ConfirmationService>();
    services.AddScoped<ExplorerWorkspaceStore>();
    services.AddScoped<FileDownloadService>();
    services.AddScoped<CannedMessagesStore>();
    services.AddScoped<AuditEntriesStore>();
    services.AddScoped<ConnectionsStore>();

    services.AddApplication(configuration);
    services.AddInfrastructure(configuration);
    services.AddAzureServiceBusProvider(configuration);

    //TODO: temp we got notification implementation in Web, so AddApplication won't register that moved from there
    services.AddMediator(options =>
    {
        options.ServiceLifetime = ServiceLifetime.Scoped;
        options.Assemblies = [typeof(IAuditLog).Assembly, typeof(Program).Assembly];
        options.PipelineBehaviors =
        [
            typeof(ErrorLoggingHandler<,>),
            typeof(ValidationHandler<,>)
        ];
    });
}

static void ConfigurePipeline(WebApplication app)
{
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
    }

    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

    app.UseAntiforgery();

    app.MapStaticAssets();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    app.MapDefaultEndpoints();
}
