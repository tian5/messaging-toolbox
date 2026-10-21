using MessagingToolbox.Application.Core.Models;

namespace MessagingToolbox.Application.Core.Interfaces;

/// <summary>
/// Read model over the connections the toolbox knows: configuration-seeded (Aspire),
/// merged with persisted <c>ConnectionProfile</c> entities.
/// </summary>
public interface IConnectionCatalog
{
    ValueTask<IReadOnlyList<ConnectionInfo>> GetAllAsync(CancellationToken cancellationToken);

    ValueTask<ConnectionInfo?> GetByIdAsync(Guid connectionId, CancellationToken cancellationToken);
}
