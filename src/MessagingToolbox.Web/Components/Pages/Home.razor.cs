using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Configuration;
using MessagingToolbox.Application.Features.Explorer.Queries.GetEntityTree;
using MessagingToolbox.Application.Features.Messages.Commands.DeadLetterMessages;
using MessagingToolbox.Application.Features.Messages.Commands.DeleteMessages;
using MessagingToolbox.Application.Features.Messages.Commands.ImportMessages;
using MessagingToolbox.Application.Features.Messages.Commands.MoveMessages;
using MessagingToolbox.Application.Features.Messages.Commands.PurgeEntity;
using MessagingToolbox.Application.Features.Messages.Commands.RequeueMessages;
using MessagingToolbox.Application.Features.Messages.Commands.ResendMessage;
using MessagingToolbox.Application.Features.Messages.Commands.SettleMessages;
using MessagingToolbox.Application.Features.Messages.Queries.ExportMessages;
using MessagingToolbox.Application.Features.Messages.Queries.GetHeldLockStates;
using MessagingToolbox.Application.Features.Messages.Queries.GetPurgePreview;
using MessagingToolbox.Application.Features.Messages.Queries.PeekMessages;
using MessagingToolbox.Application.Features.Messages.Queries.ReceiveDeferredMessages;
using MessagingToolbox.Application.Features.Messages.Queries.ReceiveForSettlementMessages;
using MessagingToolbox.Application.Features.Messages.Queries.ReceiveMessages;
using MessagingToolbox.Web.Components.Explorer;
using MessagingToolbox.Web.Components.Messages;
using MessagingToolbox.Web.Services;
using MessagingToolbox.Web.Services.Filtering;
using MessagingToolbox.Web.Services.Helpers;
using MessagingToolbox.Web.Services.Session;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;

namespace MessagingToolbox.Web.Components.Pages;

public partial class Home : IDisposable
{
    [Inject]
    private ILogger<Home> Logger { get; set; } = default!;

    [Inject]
    private IOptions<ToolboxOptions> Options { get; set; } = default!;

    [Inject]
    private PollingRefreshService PollingRefresh { get; set; } = default!;

    [Inject]
    private NotificationService Notifications { get; set; } = default!;

    [Inject]
    private ConfirmationService Confirmation { get; set; } = default!;

    [Inject]
    private ExplorerWorkspaceStore WorkspaceStore { get; set; } = default!;

    [Inject]
    private ConnectionsStore ConnectionsStore { get; set; } = default!;

    [Inject]
    private FileDownloadService FileDownload { get; set; } = default!;

    [Inject]
    private LiveWatchService LiveWatches { get; set; } = default!;

    private readonly CancellationTokenSource _disposalTokenSource = new();

    private ConnectionDto? _selectedConnection;
    private bool _autoRefresh;
    private string? _error;

    private ExplorerSidebar? _sidebar;

    // Requeue picker state (DLQ views only): the destination is a queue or topic from the tree.
    private bool _requeuePanelVisible;
    private string? _requeueTarget;
    private bool _requeueRemoveOriginals;

    // Move picker state: the destination is another queue from the tree.
    private bool _movePanelVisible;
    private string? _moveTarget;

    // State for the active connection; the shared sentinel keeps markup null-safe when no
    // connection is selected (those markup sections only render binds when one is).
    private ConnectionWorkspace ActiveConnectionWorkspace => _selectedConnection is { } connection
        ? WorkspaceStore.For(connection.Id)
        : WorkspaceStore.Empty;

    private ProviderCapabilities? Capabilities => _selectedConnection?.Capabilities;

    // Message-bearing entities only, and only when the broker can browse at all (ADR 5).
    private bool CanPeek => ActiveConnectionWorkspace.SelectedNode is { Kind: EntityKind.Queue or EntityKind.Subscription }
        && Capabilities?.CanPeek != false;

    // Live watch peeks on a timer, so it needs a side-effect-free peek and a sequence cursor (ADR 15, ADR 17).
    private bool CanWatch => CanPeek
        && ActiveConnectionWorkspace.SelectedNode is { PeekMode: PeekMode.NonDestructive }
        && CanPeekFromSequence;

    // Browsing by sequence number: every entity of a connection that has them, or an entity whose
    // own messages do (a RabbitMQ stream's offsets, ADR 24).
    private bool CanPeekFromSequence => HasSequenceNumbers(ActiveConnectionWorkspace.SelectedNode);

    private bool CanPeekFromTime => ActiveConnectionWorkspace.SelectedNode?.CanPeekFromTime == true;

    private bool HasSequenceNumbers(EntityTreeNodeDto? node) => Capabilities?.SupportsSequenceNumbers != false || node?.HasSequenceNumbers == true;

    // The start inputs belong to the connection's workspace; one the entity can't use is ignored
    // instead of failing the load. A start time wins over a start sequence number.
    private (long? Sequence, DateTimeOffset? Time) PeekStart(ConnectionWorkspace workspace, EntityTreeNodeDto node)
        => node.CanPeekFromTime && workspace.FromTime is { } time
            ? (null, new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc)))
            : (HasSequenceNumbers(node) ? workspace.FromSequence : null, null);

    // ADR 20: the entity narrows what the connection allows (a RabbitMQ stream can't be consumed away).
    private EntityOperations SelectedOperations => ActiveConnectionWorkspace.SelectedNode?.Operations ?? EntityOperations.All;

    private bool CanSend => ActiveConnectionWorkspace.SelectedNode is { SubQueue: MessageSubQueue.Active, Kind: EntityKind.Queue or EntityKind.Topic or EntityKind.Exchange }
        && SelectedOperations.CanSend;

    private bool IsScheduledNode => ActiveConnectionWorkspace.SelectedNode?.SubQueue == MessageSubQueue.Scheduled;

    private bool IsDeadLetterNode => ActiveConnectionWorkspace.SelectedNode?.SubQueue is MessageSubQueue.DeadLetter or MessageSubQueue.TransferDeadLetter;

    // Only active and scheduled messages can still be dead-lettered — the DLQ views are terminal.
    // Brokers that dead-letter through routing need the entity to have a dead-letter exchange:
    // a reject without one discards the message (ADR 19).
    private bool CanDeadLetterSelection => ActiveConnectionWorkspace.SelectedNode?.SubQueue is MessageSubQueue.Active or MessageSubQueue.Scheduled
        && (IsScheduledNode || IsSettleableLoaded)
        && HasDeadLetterDestination;

    private bool CanDeadLetterHeld => !IsDeadLetterNode && HasDeadLetterDestination;

    private bool HasDeadLetterDestination => Capabilities?.SupportsDeadLetterSubQueue != false
        || ActiveConnectionWorkspace.SelectedNode?.DeadLetterExchange is not null;

    private string DeadLetterDestination => ActiveConnectionWorkspace.SelectedNode?.DeadLetterExchange is { } exchange
        ? $"dead-letter exchange '{(exchange.Length == 0 ? "(default)" : exchange)}'"
        : "the dead-letter queue";

    // Dead-letter sub-queues replay their messages; on a broker without them any queue may be a
    // dead-letter queue, so every queue can send copies elsewhere.
    private bool CanResubmit => IsDeadLetterNode
        || (Capabilities?.SupportsDeadLetterSubQueue == false && ActiveConnectionWorkspace.SelectedNode is { Kind: EntityKind.Queue });

    // Originals are removed through held deliveries, or by a sequence scan where the broker has
    // sequence numbers (DLQ only, ADR 10).
    private bool CanRemoveOriginals => ActiveConnectionWorkspace.SelectedLockTokens.Count > 0
        || (IsDeadLetterNode && Capabilities?.SupportsSequenceNumbers != false);

    private IReadOnlyList<string> HeldLockTokens => ActiveConnectionWorkspace.Messages?
        .Where(message => message.LockToken is not null)
        .Select(message => message.LockToken!)
        .ToList() ?? [];

    private bool CanReleaseHeld => IsSettleableLoaded && Capabilities?.SupportsLockExpiry == false && HeldLockTokens.Count > 0;

    // Requeue targets are every queue and topic modeled in the tree — DLQ copies can be replayed
    // anywhere. Virtual sub-queue nodes (Active/Scheduled/DLQ) inherit the parent Kind, so they
    // are excluded explicitly.
    private IReadOnlyList<EntityTreeNodeDto> RequeueTargets => ActiveConnectionWorkspace.Tree is { } tree
        ? EntityTreeFiltering.Flatten(tree)
            .Where(node => !node.IsSubQueueNode && node.Kind is EntityKind.Queue or EntityKind.Topic or EntityKind.Exchange)
            .ToList()
        : [];

    // A broker-side move of everything in the entity (RabbitMQ shovel), where the entity allows it (ADR 23).
    private bool CanMoveAll => Capabilities?.CanMoveMessages == true
        && SelectedOperations.CanMoveMessages
        && ActiveConnectionWorkspace.SelectedNode is { Kind: EntityKind.Queue, SubQueue: MessageSubQueue.Active };

    private IReadOnlyList<EntityTreeNodeDto> MoveTargets => ActiveConnectionWorkspace.Tree is { } tree
        ? EntityTreeFiltering.Flatten(tree)
            .Where(node => !node.IsSubQueueNode && node.Kind == EntityKind.Queue && node.Path != ActiveConnectionWorkspace.SelectedNode?.Path)
            .ToList()
        : [];

    private bool IsSettleableLoaded => ActiveConnectionWorkspace.LoadedMode == MessageLoadMode.Settleable;

    private bool IsReceiveAndDeleteLoaded => ActiveConnectionWorkspace.LoadedMode == MessageLoadMode.ReceiveAndDelete;

    private bool IsSelectedEntityBusy => ActiveConnectionWorkspace.SelectedNode is { } node
        && ActiveConnectionWorkspace.IsEntityBusy(node);

    private bool IsSelectedEntityLoading => ActiveConnectionWorkspace.SelectedNode is { } node
        && ActiveConnectionWorkspace.IsEntityLoading(node);

    private string SubQueueLabel => SubQueueLabels.LabelOf(ActiveConnectionWorkspace.SelectedNode?.SubQueue ?? MessageSubQueue.Active);

    private string ComposerKey() => $"{_selectedConnection?.Id}|{ActiveConnectionWorkspace.SelectedNode?.Path}|{ActiveConnectionWorkspace.PrefillVersion}";

    private Notification Notify(string text, bool pending = false)
        => Notifications.Notify(text, pending, _selectedConnection?.DisplayName);

    protected override async Task OnInitializedAsync()
    {
        // Subscribe regardless of the configured interval: the "Auto" toggle can start polling later.
        PollingRefresh.Ticked += OnRefreshTickAsync;
        if (Options.Value.RefreshIntervalSeconds > 0)
        {
            _autoRefresh = true;
            PollingRefresh.Start(TimeSpan.FromSeconds(Options.Value.RefreshIntervalSeconds));
        }

        ConnectionsStore.Changed += OnConnectionsChangedAsync;
        await ConnectionsStore.ReloadAsync();

        WorkspaceStore.Changed += OnEntityListChangedAsync;
        Confirmation.Changed += OnConfirmationChanged;
        LiveWatches.Changed += OnLiveWatchChanged;
    }

    private void OnConfirmationChanged() => InvokeAsync(StateHasChanged);

    // Keeps the pin button in the details header in step with unpins from the watch panel.
    private Task OnLiveWatchChanged() => InvokeAsync(StateHasChanged);

    private bool IsSelectedNodeWatched => ActiveConnectionWorkspace.SelectedNode is { } node
        && LiveWatches.IsWatched(ActiveConnectionWorkspace.ConnectionId, node.Path, node.SubQueue);

    // Pinning is the user's explicit opt-in to watching; it never loads messages into the grid.
    private void ToggleWatch()
    {
        // A requeue-peek must never run on a timer (ADR 17); unpinning is always allowed.
        if (!CanWatch && !IsSelectedNodeWatched)
        {
            return;
        }

        if (_selectedConnection is { } connection && ActiveConnectionWorkspace.SelectedNode is { } node)
        {
            LiveWatches.Toggle(connection.Id, connection.DisplayName, node.Path, node.SubQueue);
        }
    }

    // Selects the watched entity (switching connection if needed) — loading its messages stays an
    // explicit Load.
    private async Task OpenWatchedAsync(LiveWatch watch)
    {
        ConnectionDto? connection = ConnectionsStore.Connections.FirstOrDefault(candidate => candidate.Id == watch.ConnectionId);
        if (connection is null)
        {
            return;
        }

        if (_selectedConnection?.Id != connection.Id)
        {
            await OnConnectionSelectedAsync(connection);
        }

        EntityTreeNodeDto? node = EntityTreeFiltering.Flatten(ActiveConnectionWorkspace.Tree ?? [])
            .Where(candidate => candidate.Path == watch.Path
                && candidate.SubQueue == watch.SubQueue
                && candidate.Kind is EntityKind.Queue or EntityKind.Subscription)
            .OrderBy(candidate => candidate.IsSubQueueNode)
            .FirstOrDefault();
        if (node is not null)
        {
            OnNodeSelected(node);
        }
    }

    // Session policy for a changed connection list: prune gone workspaces, keep (or re-pick)
    // the selection and make sure it has a tree. Triggered only by ConnectionsStore.Changed.
    private async Task OnConnectionsChangedAsync()
    {
        try
        {
            await InvokeAsync(async () =>
            {
                IReadOnlyList<ConnectionDto> connections = ConnectionsStore.Connections;

                // Drop workspace state of connections that no longer exist.
                WorkspaceStore.PruneExcept(connections.Select(connection => connection.Id));
                LiveWatches.PruneExcept(connections.Select(connection => connection.Id));

                _selectedConnection = connections.FirstOrDefault(connection => connection.Id == _selectedConnection?.Id)
                    ?? connections.FirstOrDefault();

                if (_selectedConnection is not null && ActiveConnectionWorkspace.Tree is null)
                {
                    await LoadTreeAsync(ActiveConnectionWorkspace);
                }

                StateHasChanged();
            });
        }
        catch (Exception exception)
        {
            ReportError(exception, "loading connections");
        }
    }

    // Only the active connection is refreshed; inactive workspaces update on next visit.
    private Task OnRefreshTickAsync() => InvokeAsync(() => RefreshWorkspaceAsync(ActiveConnectionWorkspace));

    // Manual refresh: the same work the auto-refresh tick does, on demand — always available,
    // whether polling is enabled or not.
    private Task RefreshNowAsync() => RefreshWorkspaceAsync(ActiveConnectionWorkspace);

    // One refresh per workspace at a time: an auto tick landing during a manual (or a slow previous)
    // refresh is skipped instead of piling more count walks onto the emulator. The flag drives the
    // sidebar's busy indicator and locks only its Refresh button.
    private async Task RefreshWorkspaceAsync(ConnectionWorkspace workspace)
    {
        if (_selectedConnection is null || workspace.IsRefreshing)
        {
            return;
        }

        workspace.IsRefreshing = true;
        StateHasChanged();

        try
        {
            await LoadTreeAsync(workspace);
            await RefreshLockStatesAsync();
        }
        finally
        {
            workspace.IsRefreshing = false;
            StateHasChanged();
        }
    }

    // Renewals happen server-side in the provider; poll the current lock expiries so the grid's
    // countdown reflects them instead of counting down from the original LockedUntil and crying
    // "expired" on a lock that was renewed. Runs on the auto-refresh tick for the active workspace.
    private async Task RefreshLockStatesAsync()
    {
        ConnectionWorkspace workspace = ActiveConnectionWorkspace;
        if (workspace.LoadedMode != MessageLoadMode.Settleable || workspace.Messages is null || workspace.SelectedNode is null)
        {
            return;
        }

        IReadOnlyList<string> tokens = workspace.SelectedLockTokens;
        if (tokens.Count == 0)
        {
            return;
        }

        try
        {
            IReadOnlyDictionary<string, DateTimeOffset> states = await Mediator.Send(
                new GetHeldLockStatesQuery(workspace.ConnectionId, workspace.SelectedNode.Path, workspace.SelectedNode.SubQueue, tokens),
                _disposalTokenSource.Token);

            Dictionary<string, DateTimeOffset> previous = workspace.Messages
                .Where(message => message.LockToken is not null && message.LockedUntil is not null)
                .ToDictionary(message => message.LockToken!, message => message.LockedUntil!.Value);

            workspace.Messages = workspace.Messages
                .Select(message => message.LockToken is not null
                    && states.TryGetValue(message.LockToken, out DateTimeOffset lockedUntil)
                    && lockedUntil != message.LockedUntil
                        ? message with { LockedUntil = lockedUntil }
                        : message)
                .ToList();

            if (states.Any(pair => previous.TryGetValue(pair.Key, out DateTimeOffset old) && pair.Value > old))
            {
                workspace.LockRenewalCount++;
            }
        }
        catch (Exception exception)
        {
            Logger.LogDebug(exception, "Refreshing held lock states failed");
        }
    }

    private void ToggleAutoRefresh()
    {
        _autoRefresh = !_autoRefresh;
        if (_autoRefresh)
        {
            // With polling disabled by default (0), the manual opt-in gets a relaxed fallback
            // interval instead of 1 s.
            int intervalSeconds = Options.Value.RefreshIntervalSeconds > 0
                ? Options.Value.RefreshIntervalSeconds
                : 10;
            PollingRefresh.Start(TimeSpan.FromSeconds(intervalSeconds));
        }
        else
        {
            PollingRefresh.Stop();
        }
    }

    private async Task OnConnectionSelectedAsync(ConnectionDto connection)
    {
        _selectedConnection = connection;
        ConnectionWorkspace workspace = WorkspaceStore.For(connection.Id);

        // Nothing is cleared here: everything from a previous visit stays in the workspace. The
        // tree is refreshed so counts and entities are current, and the selection is re-resolved
        // against the fresh tree by key.
        await LoadTreeAsync(workspace);
    }

    private async Task LoadTreeAsync(ConnectionWorkspace workspace)
    {
        if (_selectedConnection is null)
        {
            return;
        }

        try
        {
            workspace.Tree = await Mediator.Send(new GetEntityTreeQuery(workspace.ConnectionId), _disposalTokenSource.Token);
            workspace.UnavailableReason = null;
            RebindSelectedNode(workspace);
        }
        catch (MessagingProviderUnavailableException exception)
        {
            // Expected while the emulator starts: a sidebar state, not an error banner.
            workspace.Tree ??= [];
            workspace.UnavailableReason = exception.Message;
            Logger.LogInformation(exception, "Broker unreachable while loading the entity tree");
            return;
        }
        catch (Exception exception)
        {
            // A failed refresh must not wipe a previously loaded tree.
            workspace.Tree ??= [];
            ReportError(exception, "loading the entity tree");
            return;
        }

        // The provider re-probes emulator management on tree loads; pick up a capability that
        // changed since the connection list was fetched (e.g. the admin endpoint came up).
        if (_selectedConnection is { Capabilities.CanManageEntities: false } or { Capabilities: null })
        {
            await ConnectionsStore.ReloadAsync();
        }
    }

    /// <summary>
    /// The tree is rebuilt on every load, so the selected node instance goes stale — re-resolve it
    /// by key so the highlight and the details pane keep showing fresh runtime info.
    /// </summary>
    private static void RebindSelectedNode(ConnectionWorkspace workspace)
    {
        if (workspace.SelectedNode is null || workspace.Tree is null)
        {
            return;
        }

        string key = EntityTreeFiltering.NodeKey(workspace.SelectedNode);
        workspace.SelectedNode = EntityTreeFiltering.Flatten(workspace.Tree).FirstOrDefault(node => EntityTreeFiltering.NodeKey(node) == key) ?? workspace.SelectedNode;
    }

    // Selecting a node never touches the broker — messages are fetched only when the user hits Load.
    // What was already loaded is kept per node, so coming back shows it again instead of an empty grid.
    private void OnNodeSelected(EntityTreeNodeDto node)
    {
        ConnectionWorkspace workspace = ActiveConnectionWorkspace;
        workspace.StashCurrentNode();

        workspace.SelectedNode = node;
        workspace.ViewMode = AllowedViewMode(workspace.ViewMode, node.Operations);
        workspace.ClearRepairState();
        workspace.RestoreForSelectedNode();

        // The move picker belongs to the entity it was opened on.
        _movePanelVisible = false;
    }

    // The load mode is per connection, so a mode the newly selected entity refuses falls back to peek.
    private static MessageLoadMode AllowedViewMode(MessageLoadMode mode, EntityOperations operations) => mode switch
    {
        MessageLoadMode.Settleable when !operations.CanReceiveSettle => MessageLoadMode.Peek,
        MessageLoadMode.ReceiveAndDelete when !operations.CanReceive => MessageLoadMode.Peek,
        _ => mode,
    };

    private async Task LoadMessagesAsync()
    {
        if (ActiveConnectionWorkspace.ViewMode == MessageLoadMode.ReceiveAndDelete && !IsScheduledNode)
        {
            ShowConfirm(
                "Receive & delete",
                $"This permanently deletes up to {ActiveConnectionWorkspace.MaxMessages} message(s) from {ActiveConnectionWorkspace.SelectedNode!.Path} ({SubQueueLabel}){SessionScope(ActiveConnectionWorkspace.SessionFilterFor(ActiveConnectionWorkspace.SelectedNode, MessageLoadMode.ReceiveAndDelete))}. Continue?",
                LoadMessagesCoreAsync);
            return;
        }

        await LoadMessagesCoreAsync();
    }

    private async Task LoadMessagesCoreAsync()
    {
        // Capture the workspace up front: the fetch may outlive the user's stay on this
        // connection, and the result must land in the workspace it was started for.
        ConnectionWorkspace workspace = ActiveConnectionWorkspace;
        if (workspace.SelectedNode is null)
        {
            return;
        }

        // Loads lock the entity too — receive-and-delete loads are destructive.
        await RunEntityOperationAsync(workspace, () => LoadMessagesOperationAsync(workspace));
    }

    private async Task LoadMessagesOperationAsync(ConnectionWorkspace workspace)
    {
        if (workspace.SelectedNode is not { } node)
        {
            return;
        }

        workspace.BeginEntityLoad(node);
        workspace.PeekError = null;
        workspace.Selection.Clear();

        // Scheduled messages are not receivable before their enqueue time — peek is the only mode.
        // Computed from the captured node: the user may have moved on to another entity by now.
        MessageLoadMode effectiveMode = node.SubQueue == MessageSubQueue.Scheduled ? MessageLoadMode.Peek : workspace.ViewMode;
        (long? Sequence, DateTimeOffset? Time) start = PeekStart(workspace, node);

        // The fetch can run while the user moves on to another node or connection, so the
        // notification keeps its own copy of what is being fetched — several of them can be in
        // flight side by side.
        string path = node.Path;
        MessageSubQueue subQueue = node.SubQueue;
        string? sessionId = workspace.SessionFilterFor(node, effectiveMode);
        string target = $"{path} ({SubQueueLabels.LabelOf(subQueue)}){SessionScope(sessionId)}";
        Notification notification = Notify($"Fetching messages from {target}…", pending: true);
        StateHasChanged();

        try
        {
            IReadOnlyList<MessageDto> loaded = effectiveMode switch
            {
                MessageLoadMode.Settleable => await Mediator.Send(
                    new ReceiveForSettlementMessagesQuery(workspace.ConnectionId, path, subQueue, workspace.MaxMessages, null, Math.Clamp(workspace.LockRenewalSeconds, 0, 300), sessionId),
                    _disposalTokenSource.Token),
                MessageLoadMode.ReceiveAndDelete => await ReceiveAndRefreshTreeAsync(workspace, path, subQueue, sessionId),
                _ => await Mediator.Send(
                    new PeekMessagesQuery(workspace.ConnectionId, path, subQueue, start.Sequence, workspace.MaxMessages, sessionId, start.Time),
                    _disposalTokenSource.Token),
            };

            workspace.ApplyLoadedMessages(node, loaded, effectiveMode);
            Notifications.Complete(notification, $"Fetched {loaded.Count} message(s) from {target}.");
        }
        catch (Exception exception)
        {
            workspace.ApplyLoadError(node, exception.Message);
            Notifications.Complete(notification, $"Fetching messages from {target} failed: {exception.Message}");
            Logger.LogWarning(exception, "Loading messages from {Path} ({SubQueue}) failed", path, subQueue);
        }
        finally
        {
            workspace.EndEntityLoad(node);
        }
    }

    private async Task<IReadOnlyList<MessageDto>> ReceiveAndRefreshTreeAsync(ConnectionWorkspace workspace, string path, MessageSubQueue subQueue, string? sessionId)
    {
        IReadOnlyList<MessageDto> received = await Mediator.Send(
            new ReceiveMessagesQuery(workspace.ConnectionId, path, subQueue, workspace.MaxMessages, null, sessionId),
            _disposalTokenSource.Token);

        await LoadTreeAsync(workspace);
        return received;
    }

    private void ToggleSelection(string rowKey)
    {
        if (!ActiveConnectionWorkspace.Selection.Remove(rowKey))
        {
            ActiveConnectionWorkspace.Selection.Add(rowKey);
        }
    }

    // Select-all covers every message currently displayed (the filtered/sorted list), not just the visible page.
    private void ToggleAllSelection(bool selectAll)
    {
        ConnectionWorkspace workspace = ActiveConnectionWorkspace;
        workspace.Selection.Clear();
        if (!selectAll)
        {
            return;
        }

        foreach (MessageDto message in DisplayedMessages() ?? [])
        {
            workspace.Selection.Add(message.RowKey);
        }
    }

    private void SettleAsync(SettleAction action)
    {
        IReadOnlyList<string> tokens = ActiveConnectionWorkspace.SelectedLockTokens;
        if (tokens.Count == 0)
        {
            return;
        }

        if (action.Operation is SettlementOperation.Complete or SettlementOperation.DeadLetter)
        {
            ShowConfirm(
                $"{action.Operation} messages",
                $"{action.Operation} {tokens.Count} selected message(s)? This removes them from {ActiveConnectionWorkspace.SelectedNode!.Path} ({SubQueueLabel}).",
                () => SettleCoreAsync(action, tokens));
            return;
        }

        _ = SettleCoreAsync(action, tokens);
    }

    // Requeues everything this load holds (abandon), so the application under test gets it back.
    private Task ReleaseHeld()
    {
        IReadOnlyList<string> tokens = HeldLockTokens;
        return tokens.Count == 0 ? Task.CompletedTask : SettleCoreAsync(new SettleAction(SettlementOperation.Abandon, null, null), tokens);
    }

    private Task SettleCoreAsync(SettleAction action, IReadOnlyList<string> tokens)
    {
        ConnectionWorkspace workspace = ActiveConnectionWorkspace;
        return RunEntityOperationAsync(workspace, () => SettleOperationAsync(workspace, action, tokens));
    }

    private async Task SettleOperationAsync(ConnectionWorkspace workspace, SettleAction action, IReadOnlyList<string> tokens)
    {
        EntityTreeNodeDto node = workspace.SelectedNode!;
        string target = $"{workspace.SelectedNode!.Path} ({SubQueueLabels.LabelOf(workspace.SelectedNode.SubQueue)})";
        Notification notification = Notify($"{action.Operation} on {tokens.Count} message(s) in {target}…", pending: true);

        try
        {
            await Mediator.Send(
                new SettleMessagesCommand(workspace.ConnectionId, workspace.SelectedNode!.Path, workspace.SelectedNode.SubQueue, action.Operation, tokens, action.Reason, action.Description),
                _disposalTokenSource.Token);

            workspace.Selection.Clear();
            Notifications.Complete(notification, $"{action.Operation} applied to {tokens.Count} message(s).");
            HashSet<string> settled = [.. tokens];
            await RefreshAfterMutationAsync(workspace, node, message => message.LockToken is { } token && settled.Contains(token));
        }
        catch (Exception exception)
        {
            workspace.PeekError = exception.Message;
            Notifications.Complete(notification, $"{action.Operation} failed: {exception.Message}");
        }
    }

    private Task ReceiveDeferredSelected()
    {
        ConnectionWorkspace workspace = ActiveConnectionWorkspace;
        IReadOnlyList<MessageDto> deferred = workspace.SelectedDeferredMessages;
        return deferred.Count == 0
            ? Task.CompletedTask
            : RunEntityOperationAsync(workspace, () => ReceiveDeferredOperationAsync(workspace, deferred));
    }

    private async Task ReceiveDeferredOperationAsync(ConnectionWorkspace workspace, IReadOnlyList<MessageDto> deferred)
    {
        if (workspace.SelectedNode is not { } node)
        {
            return;
        }

        string target = $"{node.Path} ({SubQueueLabels.LabelOf(node.SubQueue)})";
        Notification notification = Notify($"Receiving {deferred.Count} deferred message(s) from {target}…", pending: true);

        try
        {
            IReadOnlyList<MessageDto> received = await Mediator.Send(
                new ReceiveDeferredMessagesQuery(
                    workspace.ConnectionId,
                    node.Path,
                    node.SubQueue,
                    deferred.Select(message => new DeferredMessageReference(message.SequenceNumber!.Value, message.SessionId)).ToList(),
                    Math.Clamp(workspace.LockRenewalSeconds, 0, 300)),
                _disposalTokenSource.Token);

            // The grid switches to the held messages, already selected, so the settle bar acts on
            // them right away. With nothing received the peeked view stays as it was.
            if (received.Count > 0)
            {
                workspace.ApplyLoadedMessages(node, received, MessageLoadMode.Settleable, selectAll: true);
            }

            string skipped = received.Count < deferred.Count
                ? $" {deferred.Count - received.Count} skipped: no longer deferred, locked by another receiver, or gone."
                : string.Empty;
            Notifications.Complete(notification, $"Received {received.Count} of {deferred.Count} deferred message(s) from {target} for settlement.{skipped}");
        }
        catch (Exception exception)
        {
            Notifications.Complete(notification, $"Receiving deferred messages from {target} failed: {exception.Message}");
            Logger.LogWarning(exception, "Receiving deferred messages from {Path} ({SubQueue}) failed", node.Path, node.SubQueue);
        }
    }

    private async Task PurgeAsync()
    {
        long? count;
        try
        {
            count = await Mediator.Send(
                new GetPurgePreviewQuery(ActiveConnectionWorkspace.ConnectionId, ActiveConnectionWorkspace.SelectedNode!.Path, ActiveConnectionWorkspace.SelectedNode.SubQueue),
                _disposalTokenSource.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The preview is informational (FR-5.2 allows "unknown"); it must not block the purge.
            Logger.LogWarning(exception, "Purge preview of {Path} failed", ActiveConnectionWorkspace.SelectedNode!.Path);
            count = null;
        }

        string countText = count?.ToString("N0") ?? "an unknown number of";
        ShowConfirm(
            $"Purge {ActiveConnectionWorkspace.SelectedNode.Path}",
            $"Permanently delete {countText} message(s) from {ActiveConnectionWorkspace.SelectedNode.Path} ({SubQueueLabel})? This cannot be undone.",
            PurgeCoreAsync);
    }

    private Task PurgeCoreAsync()
    {
        ConnectionWorkspace workspace = ActiveConnectionWorkspace;
        return RunEntityOperationAsync(workspace, () => PurgeOperationAsync(workspace));
    }

    private async Task PurgeOperationAsync(ConnectionWorkspace workspace)
    {
        EntityTreeNodeDto node = workspace.SelectedNode!;
        string target = $"{workspace.SelectedNode!.Path} ({SubQueueLabels.LabelOf(workspace.SelectedNode.SubQueue)})";
        Notification notification = Notify($"Purging {target}…", pending: true);

        try
        {
            long deleted = await Mediator.Send(
                new PurgeEntityCommand(workspace.ConnectionId, workspace.SelectedNode!.Path, workspace.SelectedNode.SubQueue),
                _disposalTokenSource.Token);

            Notifications.Complete(notification, $"Purged {deleted} message(s).");
            await RefreshAfterMutationAsync(workspace, node, _ => true);
        }
        catch (Exception exception)
        {
            workspace.PeekError = exception.Message;
            Notifications.Complete(notification, $"Purging {target} failed: {exception.Message}");
        }
    }

    private void OpenMove()
    {
        _moveTarget = MoveTargets.FirstOrDefault(target => target.Path == _moveTarget)?.Path ?? MoveTargets.FirstOrDefault()?.Path;
        _movePanelVisible = true;
    }

    private async Task ConfirmMoveAsync()
    {
        if (string.IsNullOrWhiteSpace(_moveTarget) || ActiveConnectionWorkspace.SelectedNode is not { } node)
        {
            return;
        }

        long? count;
        try
        {
            count = await Mediator.Send(
                new GetPurgePreviewQuery(ActiveConnectionWorkspace.ConnectionId, node.Path, node.SubQueue),
                _disposalTokenSource.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Informational only, like the purge preview.
            Logger.LogWarning(exception, "Move preview of {Path} failed", node.Path);
            count = null;
        }

        string target = _moveTarget;
        string countText = count?.ToString("N0") ?? "every";
        _movePanelVisible = false;
        ShowConfirm(
            $"Move messages from {node.Path}",
            $"Move {countText} message(s) from {node.Path} to {target}? The broker moves them itself and removes each one from {node.Path} once {target} has it. Messages held unsettled stay where they are.",
            () => MoveCoreAsync(target));
    }

    private Task MoveCoreAsync(string target)
    {
        ConnectionWorkspace workspace = ActiveConnectionWorkspace;
        return RunEntityOperationAsync(workspace, () => MoveOperationAsync(workspace, target));
    }

    private async Task MoveOperationAsync(ConnectionWorkspace workspace, string target)
    {
        EntityTreeNodeDto node = workspace.SelectedNode!;
        Notification notification = Notify($"Moving messages from {node.Path} to {target}…", pending: true);

        try
        {
            long moved = await Mediator.Send(new MoveMessagesCommand(workspace.ConnectionId, node.Path, target), _disposalTokenSource.Token);

            Notifications.Complete(notification, $"Moved {moved} message(s) from {node.Path} to {target}.");
            LiveWatches.MarkSent(workspace.ConnectionId);
            await RefreshAfterMutationAsync(workspace, node, _ => true);
        }
        catch (Exception exception)
        {
            workspace.PeekError = exception.Message;
            Notifications.Complete(notification, $"Moving messages from {node.Path} to {target} failed: {exception.Message}");
        }
    }

    private void DeleteSelected()
    {
        if (ActiveConnectionWorkspace.Selection.Count == 0)
        {
            return;
        }

        if (!IsScheduledNode && !IsSettleableLoaded)
        {
            Notify("Load messages with 'Receive (settle later)' first — deleting needs a held message lock.");
            return;
        }

        List<string> rowKeys = [.. ActiveConnectionWorkspace.Selection];
        List<long> sequences = [.. ActiveConnectionWorkspace.SelectedSequenceNumbers];
        IReadOnlyList<string> tokens = ActiveConnectionWorkspace.SelectedLockTokens;
        ShowConfirm(
            "Delete messages",
            $"Permanently delete {rowKeys.Count} selected message(s) from {ActiveConnectionWorkspace.SelectedNode!.Path} ({SubQueueLabel})? This cannot be undone.",
            () => DeleteSelectedCoreAsync(rowKeys, sequences, tokens));
    }

    private Task DeleteSelectedCoreAsync(List<string> rowKeys, List<long> sequences, IReadOnlyList<string> tokens)
    {
        ConnectionWorkspace workspace = ActiveConnectionWorkspace;
        return RunEntityOperationAsync(workspace, () => DeleteSelectedOperationAsync(workspace, rowKeys, sequences, tokens));
    }

    private async Task DeleteSelectedOperationAsync(ConnectionWorkspace workspace, List<string> rowKeys, List<long> sequences, IReadOnlyList<string> tokens)
    {
        EntityTreeNodeDto node = workspace.SelectedNode!;
        string target = $"{workspace.SelectedNode!.Path} ({SubQueueLabels.LabelOf(workspace.SelectedNode.SubQueue)})";
        Notification notification = Notify($"Deleting {rowKeys.Count} message(s) from {target}…", pending: true);

        try
        {
            int deleted = await Mediator.Send(
                new DeleteMessagesCommand(workspace.ConnectionId, workspace.SelectedNode!.Path, workspace.SelectedNode.SubQueue, sequences, tokens),
                _disposalTokenSource.Token);

            workspace.Selection.Clear();
            Notifications.Complete(notification, ResultText(deleted, rowKeys.Count, "deleted"));
            await RefreshAfterMutationAsync(workspace, node, InRows(rowKeys));
        }
        catch (Exception exception)
        {
            workspace.PeekError = exception.Message;
            Notifications.Complete(notification, $"Deleting from {target} failed: {exception.Message}");
        }
    }

    private void DeadLetterSelected()
    {
        if (ActiveConnectionWorkspace.Selection.Count == 0)
        {
            return;
        }

        if (!IsScheduledNode && !IsSettleableLoaded)
        {
            Notify("Load messages with 'Receive (settle later)' first — dead-lettering needs a held message lock.");
            return;
        }

        List<string> rowKeys = [.. ActiveConnectionWorkspace.Selection];
        List<long> sequences = [.. ActiveConnectionWorkspace.SelectedSequenceNumbers];
        IReadOnlyList<string> tokens = ActiveConnectionWorkspace.SelectedLockTokens;
        string warning = IsScheduledNode
            ? " Scheduled messages are cancelled and re-sent for immediate delivery before being dead-lettered, so they are briefly active."
            : string.Empty;

        ShowConfirm(
            "Move to dead-letter",
            $"Move {rowKeys.Count} selected message(s) from {ActiveConnectionWorkspace.SelectedNode!.Path} ({SubQueueLabel}) to {DeadLetterDestination}?{warning}",
            () => DeadLetterSelectedCoreAsync(rowKeys, sequences, tokens));
    }

    private Task DeadLetterSelectedCoreAsync(List<string> rowKeys, List<long> sequences, IReadOnlyList<string> tokens)
    {
        ConnectionWorkspace workspace = ActiveConnectionWorkspace;
        return RunEntityOperationAsync(workspace, () => DeadLetterSelectedOperationAsync(workspace, rowKeys, sequences, tokens));
    }

    private async Task DeadLetterSelectedOperationAsync(ConnectionWorkspace workspace, List<string> rowKeys, List<long> sequences, IReadOnlyList<string> tokens)
    {
        EntityTreeNodeDto node = workspace.SelectedNode!;
        string target = $"{workspace.SelectedNode!.Path} ({SubQueueLabels.LabelOf(workspace.SelectedNode.SubQueue)})";
        Notification notification = Notify($"Moving {rowKeys.Count} message(s) from {target} to {DeadLetterDestination}…", pending: true);

        try
        {
            int moved = await Mediator.Send(
                new DeadLetterMessagesCommand(workspace.ConnectionId, workspace.SelectedNode!.Path, workspace.SelectedNode.SubQueue, sequences, tokens),
                _disposalTokenSource.Token);

            workspace.Selection.Clear();
            Notifications.Complete(notification, ResultText(moved, rowKeys.Count, "dead-lettered"));
            await RefreshAfterMutationAsync(workspace, node, InRows(rowKeys));
        }
        catch (Exception exception)
        {
            workspace.PeekError = exception.Message;
            Notifications.Complete(notification, $"Dead-lettering from {target} failed: {exception.Message}");
        }
    }

    private void OpenRequeue()
    {
        if (ActiveConnectionWorkspace.Selection.Count == 0)
        {
            return;
        }

        // A dead-lettered message that records where it came from suggests its way back.
        string? source = ActiveConnectionWorkspace.SelectedMessages.Select(message => message.DeadLetterSourcePath).FirstOrDefault(path => path is not null);
        _requeueTarget = RequeueTargets.FirstOrDefault(target => target.Path == source)?.Path
            ?? _requeueTarget
            ?? RequeueTargets.FirstOrDefault()?.Path;
        _requeueRemoveOriginals = false;
        _requeuePanelVisible = true;
    }

    private void CloseRequeue() => _requeuePanelVisible = false;

    private void ConfirmRequeue()
    {
        ConnectionWorkspace workspace = ActiveConnectionWorkspace;
        if (workspace.Selection.Count == 0 || string.IsNullOrWhiteSpace(_requeueTarget))
        {
            return;
        }

        List<MessageDto> selected = [.. workspace.SelectedMessages];
        if (selected.Count == 0)
        {
            return;
        }

        string target = _requeueTarget;
        IReadOnlyList<string> tokens = workspace.SelectedLockTokens;
        bool removeOriginals = _requeueRemoveOriginals && CanRemoveOriginals;
        string removalText = removeOriginals
            ? tokens.Count > 0
                ? " The originals are then completed via their held locks."
                : " The originals are then removed from the dead-letter queue via a bounded sequence scan — any message the scan cannot match stays in the DLQ."
            : " The originals stay where they are.";

        _requeuePanelVisible = false;
        ShowConfirm(
            "Requeue messages",
            $"Send {selected.Count} selected message(s) as copies to '{target}'?{removalText}",
            () => RequeueCoreAsync(selected, tokens, target, removeOriginals));
    }

    private Task RequeueCoreAsync(List<MessageDto> selected, IReadOnlyList<string> tokens, string target, bool removeOriginals)
    {
        ConnectionWorkspace workspace = ActiveConnectionWorkspace;
        return RunEntityOperationAsync(workspace, () => RequeueOperationAsync(workspace, selected, tokens, target, removeOriginals));
    }

    private async Task RequeueOperationAsync(ConnectionWorkspace workspace, List<MessageDto> selected, IReadOnlyList<string> tokens, string target, bool removeOriginals)
    {
        EntityTreeNodeDto node = workspace.SelectedNode!;
        Notification notification = Notify($"Requeueing {selected.Count} message(s) to '{target}'…", pending: true);

        try
        {
            int sent = await Mediator.Send(
                new RequeueMessagesCommand(
                    workspace.ConnectionId,
                    workspace.SelectedNode!.Path,
                    workspace.SelectedNode.SubQueue,
                    target,
                    selected,
                    tokens,
                    removeOriginals),
                _disposalTokenSource.Token);

            workspace.Selection.Clear();
            Notifications.Complete(notification, $"Requeued {sent} message(s) to {target}.");
            LiveWatches.MarkSent(workspace.ConnectionId);
            await RefreshAfterMutationAsync(
                workspace,
                node,
                removeOriginals ? InRows([.. selected.Select(message => message.RowKey)]) : null);
        }
        catch (Exception exception)
        {
            workspace.PeekError = exception.Message;
            Notifications.Complete(notification, $"Requeueing to '{target}' failed: {exception.Message}");
        }
    }

    // Messages can vanish between the load and the operation, so report what actually happened.
    private static string ResultText(int affected, int requested, string operation)
        => affected == requested
            ? $"{affected} message(s) {operation}."
            : $"{affected} of {requested} message(s) {operation} — the rest were no longer available.";

    private Task SendCopyAsync(MessageDto message)
    {
        ConnectionWorkspace workspace = ActiveConnectionWorkspace;
        if (workspace.SelectedNode is { } node && FanOutWarning(node.Path) is { } fanOut)
        {
            ShowConfirm("Send copy", $"{fanOut} Continue?", () => RunEntityOperationAsync(workspace, () => SendCopyOperationAsync(workspace, message)));
            return Task.CompletedTask;
        }

        return RunEntityOperationAsync(workspace, () => SendCopyOperationAsync(workspace, message));
    }

    /// <summary>
    /// Sending to a subscription publishes to its topic, so every subscription whose filter
    /// matches gets the message — not just the one selected. Returns that warning, or null for
    /// a target that doesn't fan out.
    /// </summary>
    private string? FanOutWarning(string targetPath)
    {
        foreach (EntityTreeNodeDto topic in EntityTreeFiltering.Flatten(ActiveConnectionWorkspace.Tree ?? []).Where(node => node.Kind == EntityKind.Topic && !node.IsSubQueueNode))
        {
            if (topic.Children.FirstOrDefault(child => child.Kind == EntityKind.Subscription && child.Path == targetPath) is { } subscription)
            {
                return $"This publishes to topic '{topic.Name}': every subscription whose filter matches receives it, not only '{subscription.Name}'.";
            }
        }

        return null;
    }

    // Exchanges route by key, so the composer asks for one when targeting them.
    private bool IsRoutingTarget(string targetPath)
        => EntityTreeFiltering.Flatten(ActiveConnectionWorkspace.Tree ?? [])
            .Any(node => node.Kind == EntityKind.Exchange && node.Path == targetPath);

    private async Task SendCopyOperationAsync(ConnectionWorkspace workspace, MessageDto message)
    {
        try
        {
            await Mediator.Send(
                new ResendMessageCommand(
                    workspace.ConnectionId,
                    workspace.SelectedNode!.Path,
                    workspace.SelectedNode.Path,
                    workspace.SelectedNode.SubQueue,
                    message),
                _disposalTokenSource.Token);
            Notify("Copy sent — the original is untouched.");
            LiveWatches.MarkSent(workspace.ConnectionId);
            await RefreshAfterSendAsync(workspace, workspace.SelectedNode);
        }
        catch (Exception exception)
        {
            workspace.PeekError = exception.Message;
        }
    }

    private void RepairAsync(MessageDto message)
    {
        ConnectionWorkspace workspace = ActiveConnectionWorkspace;
        workspace.BeginEdit(message);
        workspace.PrefillVersion++;
    }

    private void OnCannedInsert(CannedMessageDto canned)
    {
        ConnectionWorkspace workspace = ActiveConnectionWorkspace;
        workspace.PrefillFrom(canned);
        workspace.PrefillVersion++;
    }

    // A plain send keeps the draft as it is, so the same message can be sent again; a repair is
    // done once its copy is sent, and the composer goes back to the draft.
    private async Task OnComposerSentAsync()
    {
        ConnectionWorkspace workspace = ActiveConnectionWorkspace;
        if (workspace.RepairDraft is not null)
        {
            workspace.ClearRepairState();
            workspace.PrefillVersion++;
        }

        Notify("Message sent.");
        LiveWatches.MarkSent(workspace.ConnectionId);

        await RefreshAfterSendAsync(workspace, workspace.SelectedNode);
    }

    private async Task ExportAsync()
    {
        if (ActiveConnectionWorkspace.Selection.Count == 0)
        {
            return;
        }

        try
        {
            ExportedMessagesFileDto file = await Mediator.Send(
                new ExportMessagesQuery(
                    ActiveConnectionWorkspace.ConnectionId,
                    ActiveConnectionWorkspace.SelectedNode!.Path,
                    ActiveConnectionWorkspace.SelectedNode.SubQueue,
                    ActiveConnectionWorkspace.MaxMessages,
                    CanPeekFromSequence ? ActiveConnectionWorkspace.FromSequence : null,
                    null,
                    // Exports what was loaded and selected; never fetches again (a re-peek isn't
                    // free on every broker, ADR 17).
                    ActiveConnectionWorkspace.SelectedMessages,
                    ActiveConnectionWorkspace.SessionFilterFor(ActiveConnectionWorkspace.SelectedNode, MessageLoadMode.Peek)),
                _disposalTokenSource.Token);

            await using MemoryStream stream = new(Convert.FromBase64String(file.Base64Content));
            await FileDownload.DownloadFileAsync(file.FileName, stream, _disposalTokenSource.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.LogWarning(exception, "Exporting messages failed");
            Notify($"Export failed: {exception.Message}");
        }
    }

    private async Task ImportAsync(InputFileChangeEventArgs args)
    {
        ConnectionWorkspace workspace = ActiveConnectionWorkspace;

        try
        {
            await using Stream source = args.File.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
            using MemoryStream memory = new();
            await source.CopyToAsync(memory, _disposalTokenSource.Token);

            string base64 = Convert.ToBase64String(memory.ToArray());
            if (workspace.SelectedNode is { } node && FanOutWarning(node.Path) is { } fanOut)
            {
                ShowConfirm("Import messages", $"{fanOut} Continue?", () => RunEntityOperationAsync(workspace, () => ImportOperationAsync(workspace, base64)));
                return;
            }

            await RunEntityOperationAsync(workspace, () => ImportOperationAsync(workspace, base64));
        }
        catch (Exception exception)
        {
            workspace.PeekError = exception.Message;
        }
    }

    private async Task ImportOperationAsync(ConnectionWorkspace workspace, string base64)
    {
        try
        {
            int imported = await Mediator.Send(
                new ImportMessagesCommand(workspace.ConnectionId, workspace.SelectedNode!.Path, base64),
                _disposalTokenSource.Token);

            Notify($"Imported {imported} message(s) into {workspace.SelectedNode.Path}.");
            LiveWatches.MarkSent(workspace.ConnectionId);
            await RefreshAfterSendAsync(workspace, workspace.SelectedNode);
        }
        catch (Exception exception)
        {
            workspace.PeekError = exception.Message;
        }
    }

    /// <summary>
    /// Entity-scoped operation lock: while a long-running operation (e.g. requeueing a large
    /// batch) is in flight, the entity is marked busy — its mutating controls render disabled and
    /// duplicate triggers land here and are swallowed. Other entities stay usable.
    /// </summary>
    private async Task RunEntityOperationAsync(ConnectionWorkspace workspace, Func<Task> operation)
    {
        // Capture the node up front: the user may switch nodes while the operation runs, and the
        // lock must be released for the entity it was taken on.
        EntityTreeNodeDto? node = workspace.SelectedNode;
        if (node is null || !workspace.TryBeginEntityOperation(node))
        {
            return;
        }

        StateHasChanged();
        try
        {
            await operation();
        }
        finally
        {
            workspace.EndEntityOperation(node);
            StateHasChanged();
        }
    }

    /// <summary>
    /// Every mutating operation changes broker state: refresh the tree counts and the audit log so
    /// the UI never shows stale counts (FR-2.2) — but never reload messages into the grid; only an
    /// explicit Load does that (FR-3.1). The grid drops the rows the operation is known to have
    /// removed and shows that it is stale. Only the connection the operation ran on refreshes its
    /// tree; hidden workspaces update when the user returns to them.
    /// </summary>
    private async Task RefreshAfterMutationAsync(ConnectionWorkspace workspace, EntityTreeNodeDto node, Func<MessageDto, bool>? removed)
    {
        (IReadOnlyList<MessageDto>? loaded, MessageLoadMode mode) = workspace.LoadedFor(node);
        if (mode == MessageLoadMode.Settleable && loaded is { Count: > 0 })
        {
            await KeepStillHeldMessagesAsync(workspace, node, loaded, removed);
        }
        else
        {
            workspace.MarkStale(node, removed);
        }

        if (ReferenceEquals(workspace, ActiveConnectionWorkspace))
        {
            await LoadTreeAsync(workspace);
        }
    }

    // A send lands in the entity's active (or scheduled) messages, never in a dead-letter queue, so
    // only those loads go stale; nothing loaded is removed.
    private Task RefreshAfterSendAsync(ConnectionWorkspace workspace, EntityTreeNodeDto? node)
    {
        if (node is not null && node.SubQueue is MessageSubQueue.Active or MessageSubQueue.Scheduled)
        {
            workspace.MarkStale(node);
        }

        return ReferenceEquals(workspace, ActiveConnectionWorkspace) ? LoadTreeAsync(workspace) : Task.CompletedTask;
    }

    private static Func<MessageDto, bool> InRows(IReadOnlyCollection<string> rowKeys)
    {
        HashSet<string> set = [.. rowKeys];
        return message => set.Contains(message.RowKey);
    }

    // After settling part of a settle-later load, keep the messages that are still held (with
    // current expiries) so they stay settleable; settled, abandoned or deferred ones are no longer
    // held and leave the grid. Reads lock states only — no peek, no receive.
    private async Task KeepStillHeldMessagesAsync(ConnectionWorkspace workspace, EntityTreeNodeDto node, IReadOnlyList<MessageDto> held, Func<MessageDto, bool>? removed)
    {
        List<string> tokens = held
            .Where(message => message.LockToken is not null)
            .Select(message => message.LockToken!)
            .ToList();

        try
        {
            IReadOnlyDictionary<string, DateTimeOffset> states = tokens.Count == 0
                ? new Dictionary<string, DateTimeOffset>()
                : await Mediator.Send(
                    new GetHeldLockStatesQuery(workspace.ConnectionId, node.Path, node.SubQueue, tokens),
                    _disposalTokenSource.Token);

            List<MessageDto> stillHeld = held
                .Where(message => message.LockToken is not null && states.ContainsKey(message.LockToken))
                // Brokers without expiring locks report "held" only; nothing counts down then.
                .Select(message => message with { LockedUntil = Capabilities?.SupportsLockExpiry == false ? null : states[message.LockToken!] })
                .ToList();

            workspace.ApplyLoadedMessages(node, stillHeld, MessageLoadMode.Settleable);
            workspace.MarkStale(node);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Reading held lock states of {Path} failed", node.Path);
            workspace.MarkStale(node, removed);
        }
    }

    /// <summary>
    /// Session policy after a create/update/delete changed the entity list: for the active
    /// connection, drop the selection and loaded messages, re-resolve the selection against
    /// the freshly reloaded tree and re-render. Triggered only by ExplorerWorkspaceStore.Changed;
    /// the store itself only reloads the tree.
    /// </summary>
    private async Task OnEntityListChangedAsync(Guid connectionId)
    {
        if (_selectedConnection is null || _selectedConnection.Id != connectionId)
        {
            return;
        }

        await InvokeAsync(() =>
        {
            ConnectionWorkspace workspace = ActiveConnectionWorkspace;
            workspace.SelectedNode = null;
            workspace.ClearMessages();
            RebindSelectedNode(workspace);
            StateHasChanged();
        });
    }

    private void ShowConfirm(string title, string message, Func<Task> action)
        => Confirmation.Request(title, message, action);

    private Task ConfirmAsync() => Confirmation.ConfirmAsync();

    private void CancelConfirm() => Confirmation.Cancel();

    private IReadOnlyList<EntityTreeNodeDto> FilteredTree()
        => EntityTreeFiltering.FilteredTree(ActiveConnectionWorkspace.Tree, ActiveConnectionWorkspace.SearchText);

    private static string SessionScope(string? sessionId) => sessionId is null ? string.Empty : $", session '{sessionId}'";

    private IReadOnlyList<MessageDto>? DisplayedMessages()
        => MessageFiltering.Apply(ActiveConnectionWorkspace.Messages, ActiveConnectionWorkspace.PropertyFilter, ActiveConnectionWorkspace.SortDescending);

    private void ReportError(Exception exception, string operation)
    {
        _error = $"Error {operation}: {exception.Message}";
        Logger.LogWarning(exception, "Error {Operation}", operation);
    }

    public void Dispose()
    {
        PollingRefresh.Ticked -= OnRefreshTickAsync;
        PollingRefresh.Stop();

        ConnectionsStore.Changed -= OnConnectionsChangedAsync;
        WorkspaceStore.Changed -= OnEntityListChangedAsync;
        Confirmation.Changed -= OnConfirmationChanged;
        LiveWatches.Changed -= OnLiveWatchChanged;

        _disposalTokenSource.Cancel();
        _disposalTokenSource.Dispose();
    }
}
