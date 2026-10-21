using Mediator;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Features.Connections.Notifications;
using MessagingToolbox.Application.Features.Connections.Queries.GetConnections;

namespace MessagingToolbox.Web.Services;

/// <summary>
/// Scoped (per circuit) owner of the saved connection list. Components subscribe to
/// Changed and re-render via InvokeAsync(StateHasChanged); mutations go through Mediator.
/// </summary>
public sealed class ConnectionsStore(ISender sender) : INotificationHandler<SavedConnectionChangedNotification>
{
    public IReadOnlyList<ConnectionDto> Connections { get; private set; } = [];

    public event Func<Task>? Changed;

    public ValueTask Handle(SavedConnectionChangedNotification notification, CancellationToken cancellationToken)
    {
        return ReloadAsync();
    }

    public async ValueTask ReloadAsync()
    {
        Connections = await sender.Send(new GetConnectionsQuery());
        if (Changed is not null)
        {
            await Changed.Invoke();
        }
    }
}
