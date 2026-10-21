using Mediator;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Features.Audit.Notifications;
using MessagingToolbox.Application.Features.Audit.Queries.GetAuditEntries;

namespace MessagingToolbox.Web.Services;

/// <summary>
/// Single source of truth for the audit panel (NFR-3). Scoped per circuit.
/// Mutations anywhere in the app call ReloadAsync; subscribers re-render on Changed.
/// </summary>
public sealed class AuditEntriesStore(ISender sender) : INotificationHandler<AuditEntryWrittenNotification>
{
    public IReadOnlyList<AuditEntryDto> Entries { get; private set; } = [];

    public event Func<Task>? Changed;

    private bool _loaded;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) { return; }
        _loaded = true;
        await ReloadAsync();
    }

    public ValueTask Handle(AuditEntryWrittenNotification notification, CancellationToken cancellationToken)
    {
        _loaded = true;
        return ReloadAsync();
    }

    private async ValueTask ReloadAsync()
    {
        Entries = await sender.Send(new GetAuditEntriesQuery());
        if (Changed is not null)
        {
            await Changed.Invoke();
        }
    }
}
