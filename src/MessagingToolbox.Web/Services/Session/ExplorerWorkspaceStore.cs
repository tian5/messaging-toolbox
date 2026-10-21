using Mediator;
using MessagingToolbox.Application.Features.Explorer.Queries.GetEntityTree;
using MessagingToolbox.Application.Features.Management.Notifications;
using MessagingToolbox.Web.Components.Pages;

namespace MessagingToolbox.Web.Services.Session;

/// <summary>
/// Per-circuit store of <see cref="ConnectionWorkspace"/> instances. One workspace exists per
/// connection the user has touched — switching connections swaps which workspace is active
/// instead of discarding state. Also reloads a connection's entity tree when a management
/// command reports the entity list changed; selection/message policy stays with subscribers.
/// </summary>
public sealed class ExplorerWorkspaceStore(ISender sender, ILogger<ExplorerWorkspaceStore> logger)
    : INotificationHandler<EntityListChangedNotification>
{
    private readonly Dictionary<Guid, ConnectionWorkspace> _workspaces = [];

    // Shared sentinel keeps markup null-safe when no connection is selected (those markup sections
    // only render binds when one is).
    public ConnectionWorkspace Empty { get; } = new() { ConnectionId = Guid.Empty };

    public event Func<Guid, Task>? Changed;

    public ConnectionWorkspace For(Guid connectionId)
        => _workspaces.TryGetValue(connectionId, out ConnectionWorkspace? workspace)
            ? workspace
            : _workspaces[connectionId] = new ConnectionWorkspace { ConnectionId = connectionId };

    /// <summary>Drop workspace state of connections that no longer exist.</summary>
    public void PruneExcept(IEnumerable<Guid> validIds)
    {
        List<Guid> staleIds = _workspaces.Keys.Except(validIds).ToList();
        foreach (Guid staleId in staleIds)
        {
            _workspaces.Remove(staleId);
        }
    }

    public async ValueTask Handle(EntityListChangedNotification notification, CancellationToken cancellationToken)
    {
        ConnectionWorkspace workspace = For(notification.ConnectionId);

        try
        {
            workspace.Tree = await sender.Send(new GetEntityTreeQuery(notification.ConnectionId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed reload must not wipe a previously loaded tree.
            logger.LogWarning(ex, "Reloading the entity tree for connection {ConnectionId} failed", notification.ConnectionId);
        }

        if (Changed is not null)
        {
            await Changed.Invoke(notification.ConnectionId);
        }
    }
}
