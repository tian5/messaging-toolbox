using MessagingToolbox.Application.Configuration;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MessagingToolbox.Infrastructure.Persistence;

/// <summary>
/// Merges the two connection sources (FR-1.1/1.4): config-seeded entries (Aspire/env, read-only)
/// and saved profiles from the toolbox database (protected at rest). Reads are always fresh —
/// profile counts are tiny, and the catalog is a singleton while DbContexts are not.
/// </summary>
public sealed class ConnectionCatalog : IConnectionCatalog
{
    private readonly IOptions<ToolboxOptions> _options;
    private readonly IConfiguration _configuration;
    private readonly IDbContextFactory<ToolboxDbContext> _dbContextFactory;
    private readonly IConnectionStringProtector _protector;
    private readonly ILogger<ConnectionCatalog> _logger;

    public ConnectionCatalog(
        IOptions<ToolboxOptions> options,
        IConfiguration configuration,
        IDbContextFactory<ToolboxDbContext> dbContextFactory,
        IConnectionStringProtector protector,
        ILogger<ConnectionCatalog> logger)
    {
        _options = options;
        _configuration = configuration;
        _dbContextFactory = dbContextFactory;
        _protector = protector;
        _logger = logger;
    }

    public async ValueTask<IReadOnlyList<ConnectionInfo>> GetAllAsync(CancellationToken cancellationToken)
    {
        List<ConnectionInfo> connections = [.. GetConfigConnections()];

        try
        {
            await using ToolboxDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            List<ConnectionProfile> profiles = await dbContext.ConnectionProfiles
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            foreach (ConnectionProfile profile in profiles)
            {
                ConnectionInfo? connection = GetConnectionInfo(profile);
                if (connection is not null)
                {
                    connections.Add(connection);
                }
            }
        }
        catch (Exception exception)
        {
            // The database may not be ready/available; config-seeded connections must still work.
            _logger.LogError(exception, "Saved connections are unavailable; using config-seeded connections only");
        }

        return connections;
    }

    public async ValueTask<ConnectionInfo?> GetByIdAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        ConnectionInfo? configConnection = GetConfigConnections()
            .FirstOrDefault(connection => connection.Id == connectionId);

        if (configConnection is not null)
        {
            return configConnection;
        }

        try
        {
            await using ToolboxDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            ConnectionProfile? profile = await dbContext.ConnectionProfiles
                .AsNoTracking()
                .SingleOrDefaultAsync(existing => existing.Id == connectionId, cancellationToken);

            return profile is null
                ? null
                : GetConnectionInfo(profile);
        }
        catch (Exception exception)
        {
            // Same resilience contract as GetAllAsync: an unavailable store means "not found",
            // never a failure leaking into provider resolution or auditing.
            _logger.LogError(exception, "Saved connections are unavailable while resolving connection '{ConnectionId}'", connectionId);
            return null;
        }
    }

    private IEnumerable<ConnectionInfo> GetConfigConnections()
    {
        foreach (PreconfiguredConnectionOptions configured in _options.Value.Connections)
        {
            string? connectionString = configured.ConnectionString
                ?? _configuration.GetConnectionString(configured.Name);

            if (string.IsNullOrWhiteSpace(configured.Name) || string.IsNullOrWhiteSpace(connectionString))
            {
                continue;
            }

            yield return new ConnectionInfo
            {
                Id = ConnectionInfo.StableIdForName(configured.Name),
                Name = configured.Name,
                DisplayName = string.IsNullOrWhiteSpace(configured.DisplayName) ? configured.Name : configured.DisplayName,
                Kind = configured.Kind,
                ConnectionString = connectionString,
                Source = ConnectionSource.Config,
                AdminEndpoint = configured.AdminEndpoint,
                ModeledQueues = configured.Queues,
                ModeledTopics = configured.Topics
                    .Select(topic => new ModeledTopicInfo(topic.Name, topic.Subscriptions))
                    .ToList(),
            };
        }
    }

    private ConnectionInfo? GetConnectionInfo(ConnectionProfile profile)
    {
        try
        {
            return new ConnectionInfo
            {
                Id = profile.Id,
                Name = profile.DisplayName,
                DisplayName = profile.DisplayName,
                Kind = profile.Kind,
                ConnectionString = _protector.Unprotect(profile.ProtectedConnectionString),
                Source = ConnectionSource.Saved,
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Skipping saved connection '{DisplayName}': connection string could not be unprotected", profile.DisplayName);
            return null;
        }
    }
}
