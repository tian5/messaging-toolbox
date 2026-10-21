using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MessagingToolbox.Infrastructure.Persistence;

/// <summary>
/// Creates the SQLite schema on startup. A failure must not take the host down — config-seeded
/// (Aspire) connections keep working without a database.
/// TODO: switch to Database.MigrateAsync() with real migrations once the schema stabilizes.
/// </summary>
public sealed class ToolboxDbInitializer : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ToolboxDbInitializer> _logger;

    public ToolboxDbInitializer(IServiceProvider serviceProvider, ILogger<ToolboxDbInitializer> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
            ToolboxDbContext dbContext = scope.ServiceProvider.GetRequiredService<ToolboxDbContext>();
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to initialize the toolbox database; saved connections, canned messages, and the audit log will be unavailable");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
