using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Web.Services.Filtering;
using MessagingToolbox.Web.Services.Session;

namespace MessagingToolbox.Web.Components.Pages;

/// <summary>
/// Per-connection UI state for the explorer page. One workspace exists per connection the user
/// has touched, so switching connections never discards fetched messages, selections, filters,
/// or repair state — coming back shows exactly what was there. Message state keys are scoped to
/// a workspace, so identical queue names on different connections can no longer collide.
/// </summary>
public sealed class ConnectionWorkspace
{
    public required Guid ConnectionId { get; init; }

    public IReadOnlyList<EntityTreeNodeDto>? Tree { get; set; }

    public EntityTreeNodeDto? SelectedNode { get; set; }

    public IReadOnlyList<MessageDto>? Messages { get; set; }

    public MessageDto? SelectedMessage { get; set; }

    /// <summary>Loaded messages survive navigating away and back — switching nodes must never discard a fetch.</summary>
    public Dictionary<string, MessageViewState> MessageStates { get; } = [];

    /// <summary>Row keys (<see cref="MessageDto.RowKey"/>) of the selected messages.</summary>
    public HashSet<string> Selection { get; set; } = [];

    public string? PeekError { get; set; }

    /// <summary>
    /// True when the user changed the broker since this load (settle, delete, send, …): the grid
    /// shows the load as it was, minus rows known to be gone, until the user loads again.
    /// Messages are never reloaded automatically.
    /// </summary>
    public bool IsStale { get; private set; }

    /// <summary>
    /// A tree/count refresh (manual or auto) is in flight for this connection. Per workspace, so the
    /// indicator stays with the connection that is refreshing when the user switches connections.
    /// </summary>
    public bool IsRefreshing { get; set; }

    /// <summary>
    /// Set when the last tree load found the broker unreachable (e.g. the emulator is still
    /// starting); cleared by the next successful load. The sidebar shows it instead of a dead tree.
    /// </summary>
    public string? UnavailableReason { get; set; }

    // Entities with a message load in flight. Keyed by node key, so a fetch shows "Loading…" and
    // disables Load only on the entity being fetched — other entities keep their loaded messages.
    private readonly HashSet<string> _loadingEntities = [];

    public bool IsEntityLoading(EntityTreeNodeDto node) => _loadingEntities.Contains(EntityTreeFiltering.NodeKey(node));

    public void BeginEntityLoad(EntityTreeNodeDto node) => _loadingEntities.Add(EntityTreeFiltering.NodeKey(node));

    public void EndEntityLoad(EntityTreeNodeDto node) => _loadingEntities.Remove(EntityTreeFiltering.NodeKey(node));

    // Entities with a long-running operation in flight (requeue, purge, settle, …). Keyed by node
    // key, so a busy entity locks only its own controls — other entities stay usable.
    private readonly HashSet<string> _busyEntities = [];

    public bool IsEntityBusy(EntityTreeNodeDto node) => _busyEntities.Contains(EntityTreeFiltering.NodeKey(node));

    /// <summary>Returns false when the entity is already busy — the duplicate trigger must be swallowed.</summary>
    public bool TryBeginEntityOperation(EntityTreeNodeDto node) => _busyEntities.Add(EntityTreeFiltering.NodeKey(node));

    public void EndEntityOperation(EntityTreeNodeDto node) => _busyEntities.Remove(EntityTreeFiltering.NodeKey(node));

    /// <summary>
    /// A message load started on <paramref name="node"/> finished. The result belongs to that node
    /// no matter what is selected now: store it under the node's key and refresh the live view only
    /// when the node is still selected — a fetch may complete after the user moved on, and its
    /// messages must never land on another entity.
    /// </summary>
    public void ApplyLoadedMessages(EntityTreeNodeDto node, IReadOnlyList<MessageDto> loaded, MessageLoadMode mode, bool selectAll = false)
    {
        IReadOnlyList<MessageDto> messages = [.. loaded.Select((message, index) => message with { RowKey = RowKeyOf(message, index) })];
        HashSet<string> selection = selectAll ? [.. messages.Select(message => message.RowKey)] : [];
        MessageViewState state = new(messages, null, selection, null, mode);
        MessageStates[EntityTreeFiltering.NodeKey(node)] = state;

        if (IsSelected(node))
        {
            Messages = state.Messages;
            SelectedMessage = null;
            Selection = state.Selection;
            PeekError = null;
            LoadedMode = mode;
            IsStale = false;
        }
    }

    /// <summary>
    /// The user's own operation changed <paramref name="node"/> on the broker. Instead of reloading,
    /// drop the rows the operation is known to have removed (<paramref name="removed"/>) and mark
    /// the load stale. A node without loaded messages stays as it is.
    /// </summary>
    public void MarkStale(EntityTreeNodeDto node, Func<MessageDto, bool>? removed = null)
    {
        string key = EntityTreeFiltering.NodeKey(node);
        if (IsSelected(node))
        {
            if (Messages is null)
            {
                return;
            }

            if (removed is not null)
            {
                Messages = [.. Messages.Where(message => !removed(message))];
                if (SelectedMessage is not null && removed(SelectedMessage))
                {
                    SelectedMessage = null;
                }

                HashSet<string> remaining = [.. Messages.Select(message => message.RowKey)];
                Selection.RemoveWhere(key => !remaining.Contains(key));
            }

            IsStale = true;
            return;
        }

        if (MessageStates.TryGetValue(key, out MessageViewState? state))
        {
            IReadOnlyList<MessageDto> kept = removed is null ? state.Messages : [.. state.Messages.Where(message => !removed(message))];
            MessageStates[key] = state with { Messages = kept, IsStale = true };
        }
    }

    /// <summary>Like <see cref="ApplyLoadedMessages"/> for a failed load: the error belongs to the node that was loaded.</summary>
    public void ApplyLoadError(EntityTreeNodeDto node, string error)
    {
        MessageStates[EntityTreeFiltering.NodeKey(node)] = new MessageViewState([], null, [], error, MessageLoadMode.Peek);

        if (IsSelected(node))
        {
            Messages = [];
            SelectedMessage = null;
            Selection = [];
            PeekError = error;
            IsStale = false;
        }
    }

    /// <summary>What is loaded for <paramref name="node"/>: the live view when it is selected, else its stashed state.</summary>
    public (IReadOnlyList<MessageDto>? Messages, MessageLoadMode Mode) LoadedFor(EntityTreeNodeDto node)
    {
        if (IsSelected(node))
        {
            return (Messages, LoadedMode);
        }

        return MessageStates.TryGetValue(EntityTreeFiltering.NodeKey(node), out MessageViewState? state)
            ? (state.Messages, state.LoadMode)
            : (null, MessageLoadMode.Peek);
    }

    private bool IsSelected(EntityTreeNodeDto node)
        => SelectedNode is not null && EntityTreeFiltering.NodeKey(SelectedNode) == EntityTreeFiltering.NodeKey(node);

    public MessageLoadMode ViewMode { get; set; } = MessageLoadMode.Peek;

    public MessageLoadMode LoadedMode { get; set; }

    public int MaxMessages { get; set; } = 100;

    /// <summary>How long (seconds, 0-300) the provider renews peek-locks after a settle-later receive; 0 = no renewal.</summary>
    public int LockRenewalSeconds { get; set; } = 60;

    /// <summary>Times a poll observed a held lock's expiry move forward — background renewals happened.</summary>
    public int LockRenewalCount { get; set; }

    public long? FromSequence { get; set; }

    /// <summary>
    /// Session filter for loads; blank means every session. Peeks filter while paging (nothing is
    /// locked); receives lock just this session.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// The session filter that applies when loading <paramref name="node"/> in <paramref name="mode"/>.
    /// Dead-letter and scheduled views aren't session-bound, so there only a peek filters.
    /// </summary>
    public string? SessionFilterFor(EntityTreeNodeDto node, MessageLoadMode mode)
        => string.IsNullOrWhiteSpace(SessionId) || (node.SubQueue != MessageSubQueue.Active && mode != MessageLoadMode.Peek)
            ? null
            : SessionId.Trim();

    /// <summary>Distinct session ids among the loaded messages — suggestions for the session filter.</summary>
    public IReadOnlyList<string> LoadedSessionIds => Messages?
        .Select(message => message.SessionId)
        .OfType<string>()
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .Take(50)
        .ToList() ?? [];

    public string? SearchText { get; set; }

    public bool SortDescending { get; set; }

    public string? PropertyFilter { get; set; }

    /// <summary>The composer's draft for this connection — kept across sends and node switches.</summary>
    public ComposerDraft Draft { get; } = new();

    /// <summary>The draft of a repair &amp; resubmit in progress; null outside repair mode.</summary>
    public ComposerDraft? RepairDraft { get; private set; }

    public MessageDto? EditMessage { get; set; }

    public string? OriginalPath { get; set; }

    public MessageSubQueue OriginalSubQueue { get; set; }

    public string? OriginalLockToken { get; set; }

    public long? OriginalSequenceNumber { get; set; }

    public int PrefillVersion { get; set; }

    /// <summary>
    /// Lock tokens of the selected messages, resolved in a single pass so a render can bind both
    /// the count and the settle actions without re-running the same LINQ query. Only messages
    /// received for settlement carry tokens.
    /// </summary>
    public IReadOnlyList<string> SelectedLockTokens => SelectedMessages
        .Where(message => message.LockToken is not null && message.LockToken != Guid.Empty.ToString())
        .Select(message => message.LockToken!)
        .ToList();

    /// <summary>The selected messages, in load order.</summary>
    public IReadOnlyList<MessageDto> SelectedMessages => Messages is null || Selection.Count == 0
        ? []
        : Messages.Where(message => Selection.Contains(message.RowKey)).ToList();

    /// <summary>Sequence numbers of the selected messages, for brokers that have them (scheduled cancels, audit).</summary>
    public IReadOnlyList<long> SelectedSequenceNumbers => SelectedMessages
        .Where(message => message.SequenceNumber is not null)
        .Select(message => message.SequenceNumber!.Value)
        .ToList();

    /// <summary>
    /// Selected deferred messages that aren't held yet — the ones a receive by sequence number can
    /// get back. Received ones carry a lock token and settle like any other held message.
    /// </summary>
    public IReadOnlyList<MessageDto> SelectedDeferredMessages => SelectedMessages
        .Where(message => message.State == MessageState.Deferred
            && message.LockToken is null
            && message.SequenceNumber is not null)
        .ToList();

    // Unique within one load: the sequence number where the broker has one, else the lock token,
    // else the position in the load.
    private static string RowKeyOf(MessageDto message, int index) => message switch
    {
        { SequenceNumber: { } sequence } => $"s:{sequence}",
        { LockToken: { } token } when token != Guid.Empty.ToString() => $"l:{token}",
        _ => $"r:{index}",
    };

    /// <summary>
    /// Put the message into repair mode against the currently selected node. <see cref="EditMessage"/>
    /// doubles as the snapshot of the original, so it stays paired with <see cref="OriginalLockToken"/>
    /// even when the user selects another row while the composer is open.
    /// </summary>
    public void BeginEdit(MessageDto message)
    {
        EditMessage = message;
        OriginalPath = SelectedNode!.Path;
        OriginalSubQueue = SelectedNode.SubQueue;
        OriginalLockToken = message.LockToken;
        OriginalSequenceNumber = message.SequenceNumber;
        RepairDraft = new ComposerDraft();
    }

    /// <summary>
    /// Insert a canned message into the composer draft, leaving repair mode; the caller bumps
    /// <see cref="PrefillVersion"/> so the editor shows the new body.
    /// </summary>
    public void PrefillFrom(CannedMessageDto canned)
    {
        ClearRepairState();
        Draft.ApplyCanned(canned);
    }

    public void StashCurrentNode()
    {
        if (SelectedNode is null || Messages is null)
        {
            return;
        }

        MessageStates[EntityTreeFiltering.NodeKey(SelectedNode)] = new MessageViewState(
            Messages,
            SelectedMessage,
            Selection,
            PeekError,
            LoadedMode,
            IsStale);
    }

    public void RestoreForSelectedNode()
    {
        if (SelectedNode is not null && MessageStates.TryGetValue(EntityTreeFiltering.NodeKey(SelectedNode), out MessageViewState? state))
        {
            Messages = state.Messages;
            SelectedMessage = state.SelectedMessage;
            Selection = state.Selection;
            PeekError = state.PeekError;
            LoadedMode = state.LoadMode;
            IsStale = state.IsStale;
            return;
        }

        Messages = null;
        SelectedMessage = null;
        Selection = [];
        PeekError = null;
        IsStale = false;
    }

    /// <summary>Drop the loaded messages, selection and repair state of the current node.</summary>
    public void ClearMessages()
    {
        Messages = null;
        SelectedMessage = null;
        PeekError = null;
        Selection = [];
        LoadedMode = MessageLoadMode.Peek;
        IsStale = false;
        ClearRepairState();
    }

    public void ClearRepairState()
    {
        RepairDraft = null;
        EditMessage = null;
        OriginalPath = null;
        OriginalLockToken = null;
        OriginalSequenceNumber = null;
    }
}

public enum MessageLoadMode
{
    Peek,

    /// <summary>Peek-lock receive: messages stay in the queue and can be settled (locks expire after the entity lock duration).</summary>
    Settleable,
    ReceiveAndDelete,
}

public sealed record MessageViewState(
    IReadOnlyList<MessageDto> Messages,
    MessageDto? SelectedMessage,
    HashSet<string> Selection,
    string? PeekError,
    MessageLoadMode LoadMode,
    bool IsStale = false);
