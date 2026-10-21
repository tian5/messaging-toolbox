using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using Microsoft.Extensions.Logging;

namespace MessagingToolbox.Application.Features.Connections.Queries.GetConnections;

public sealed class GetConnectionsQueryHandler : IRequestHandler<GetConnectionsQuery, IReadOnlyList<ConnectionDto>>
{
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly ILogger<GetConnectionsQueryHandler> _logger;

    public GetConnectionsQueryHandler(
        IConnectionCatalog connectionCatalog,
        IMessagingProviderFactory providerFactory,
        ILogger<GetConnectionsQueryHandler> logger)
    {
        _connectionCatalog = connectionCatalog;
        _providerFactory = providerFactory;
        _logger = logger;
    }

    public async ValueTask<IReadOnlyList<ConnectionDto>> Handle(GetConnectionsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<ConnectionInfo> connections = await _connectionCatalog.GetAllAsync(cancellationToken);

        List<ConnectionDto> result = [];
        foreach (ConnectionInfo connection in connections)
        {
            ProviderCapabilities? capabilities = await TryGetCapabilitiesAsync(connection.Id, cancellationToken);
            result.Add(new ConnectionDto(connection.Id, connection.Name, connection.DisplayName, connection.Kind, connection.Source, capabilities));
        }

        return result;
    }

    private async ValueTask<ProviderCapabilities?> TryGetCapabilitiesAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        try
        {
            IMessagingProvider provider = await _providerFactory.GetProviderAsync(connectionId, cancellationToken);
            return provider.Capabilities;
        }
        catch (Exception ex)
        {
            // A broken connection must not take down the whole list; the UI shows its
            // capabilities as unavailable and disables capability-gated actions.
            _logger.LogError(ex, "Failed to get capabilities for connection {ConnectionId}", connectionId);
            return null;
        }
    }
}
