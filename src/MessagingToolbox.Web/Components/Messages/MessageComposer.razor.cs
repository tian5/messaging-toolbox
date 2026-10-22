using System.Globalization;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Features.Messages.Commands.RepairAndResubmitMessage;
using MessagingToolbox.Application.Features.Messages.Commands.SendMessage;
using MessagingToolbox.Application.Features.Templates.Queries.RenderTemplate;
using MessagingToolbox.Web.Services;
using MessagingToolbox.Web.Services.Session;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace MessagingToolbox.Web.Components.Messages;

public partial class MessageComposer : IAsyncDisposable
{
    [Inject]
    private ILogger<MessageComposer> Logger { get; set; } = default!;

    [Inject]
    private ConfirmationService Confirmation { get; set; } = default!;

    [Inject]
    private CannedMessagesStore CannedStore { get; set; } = default!;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    private readonly CancellationTokenSource _disposalTokenSource = new();
    private IJSObjectReference? _timeModule;

    [Parameter]
    public Guid ConnectionId { get; set; }

    [Parameter]
    public string EntityPath { get; set; } = string.Empty;

    /// <summary>The composer's state; it outlives this component (see <see cref="ComposerDraft"/>).</summary>
    [Parameter, EditorRequired]
    public ComposerDraft Draft { get; set; } = default!;

    /// <summary>Prefill for repair; applied once per draft.</summary>
    [Parameter]
    public MessageDto? Prefill { get; set; }

    /// <summary>The unedited original message for repair &amp; resubmit; null for a plain composer.</summary>
    [Parameter]
    public MessageDto? OriginalMessage { get; set; }

    /// <summary>Original message context for repair &amp; resubmit; null for a plain composer.</summary>
    [Parameter]
    public string? OriginalPath { get; set; }

    [Parameter]
    public MessageSubQueue OriginalSubQueue { get; set; }

    [Parameter]
    public string? OriginalLockToken { get; set; }

    /// <summary>Sequence number of the original; peeked messages carry no lock token, so removal needs a settleable load.</summary>
    [Parameter]
    public long? OriginalSequenceNumber { get; set; }

    [Parameter]
    public EventCallback OnSent { get; set; }

    /// <summary>The target requires sessions: the session id field is shown up front and marked required.</summary>
    [Parameter]
    public bool RequiresSession { get; set; }

    /// <summary>The active connection's capabilities; fields the broker can't honor are hidden. Null while unknown.</summary>
    [Parameter]
    public ProviderCapabilities? Capabilities { get; set; }

    /// <summary>Explains a target that fans out (a subscription publishes to its whole topic); null when it doesn't.</summary>
    [Parameter]
    public Func<string, string?>? FanOutWarning { get; set; }

    private bool IsRepair => OriginalPath is not null;

    private bool CanRemoveOriginal => IsRepair && OriginalLockToken is not null;

    private bool _showCannedSave;
    private string? _cannedName;
    private string? _cannedFolder;
    private string? _preview;
    private bool _sending;
    private bool _saving;
    private string? _error;
    private bool _removeOriginal;

    private string TargetPath => string.IsNullOrWhiteSpace(Draft.TargetPath) ? EntityPath : Draft.TargetPath.Trim();

    private string EditorLanguage
        => Draft.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true ? "json"
            : Draft.ContentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true ? "xml"
            : "plaintext";

    protected override void OnParametersSet()
    {
        if (Draft.Follow(EntityPath))
        {
            Draft.ShowSystemProperties |= RequiresSession;
        }

        if (Prefill is not null && !ReferenceEquals(Prefill, Draft.AppliedPrefill))
        {
            Draft.ApplyMessage(Prefill);
        }
    }

    private void AddProperty() => Draft.Properties.Add(new ComposerPropertyRow());

    private static string ValuePlaceholder(PropertyValueType type) => type switch
    {
        PropertyValueType.Boolean => "true / false",
        PropertyValueType.DateTime => "2026-01-31T12:00:00Z",
        PropertyValueType.Guid => "~newGuid~ or a GUID",
        PropertyValueType.String => "value",
        _ => "number or ~randomInt~",
    };

    private async Task<SendMessageCommand> BuildSendCommandAsync()
        => new(
            ConnectionId,
            TargetPath,
            Draft.Body ?? string.Empty,
            EmptyToNull(Draft.ContentType),
            Draft.PropertyValues(),
            EmptyToNull(Draft.Subject),
            EmptyToNull(Draft.MessageId),
            EmptyToNull(Draft.CorrelationId),
            EmptyToNull(Draft.SessionId),
            EmptyToNull(Draft.To),
            EmptyToNull(Draft.ReplyTo),
            await ToUtcAsync(Draft.ScheduledLocal),
            Draft.TimeToLiveSeconds is > 0 ? TimeSpan.FromSeconds(Draft.TimeToLiveSeconds.Value) : null,
            Math.Max(1, Draft.Copies),
            Draft.PropertyTypes());

    private RepairAndResubmitMessageCommand BuildRepairCommand(bool removeOriginal)
        => new(
            ConnectionId,
            TargetPath,
            Draft.Body ?? string.Empty,
            EmptyToNull(Draft.ContentType),
            Draft.PropertyValues(),
            EmptyToNull(Draft.Subject),
            EmptyToNull(Draft.CorrelationId),
            EmptyToNull(Draft.SessionId),
            OriginalMessage,
            OriginalPath,
            OriginalSubQueue,
            OriginalLockToken,
            OriginalSequenceNumber,
            removeOriginal,
            Draft.PropertyTypes());

    private async Task SendAsync()
    {
        _sending = true;
        _error = null;

        try
        {
            await Mediator.Send(await BuildSendCommandAsync(), _disposalTokenSource.Token);

            _preview = null;
            await OnSent.InvokeAsync();
        }
        catch (Exception exception)
        {
            _error = exception.Message;
            Logger.LogWarning(exception, "Sending message to {Path} failed", TargetPath);
        }
        finally
        {
            _sending = false;
        }
    }

    // Removing the original stays an explicit, confirmed step (FR-4.6) — the checkbox only arms it.
    private async Task SendRepairAsync()
    {
        if (_removeOriginal && CanRemoveOriginal)
        {
            Confirmation.Request(
                "Send & remove original",
                "The edited copy will be sent, then the original message is completed (removed). This cannot be undone.",
                () => SendRepairCoreAsync(true));
            return;
        }

        await SendRepairCoreAsync(false);
    }

    private async Task SendRepairCoreAsync(bool removeOriginal)
    {
        _sending = true;
        _error = null;

        try
        {
            await Mediator.Send(BuildRepairCommand(removeOriginal), _disposalTokenSource.Token);

            await OnSent.InvokeAsync();
        }
        catch (Exception exception)
        {
            _error = exception.Message;
            Logger.LogWarning(exception, "Repair & resubmit to {Path} failed", TargetPath);
        }
        finally
        {
            _sending = false;
        }
    }

    private async Task PreviewAsync()
    {
        _error = null;

        try
        {
            _preview = await Mediator.Send(new RenderTemplateQuery(Draft.Body ?? string.Empty), _disposalTokenSource.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _error = exception.Message;
            Logger.LogWarning(exception, "Rendering the template preview failed");
        }
    }

    // Opening the save panel after inserting a canned message offers to update that one.
    private void ToggleCannedSave()
    {
        _showCannedSave = !_showCannedSave;
        if (_showCannedSave && Draft.CannedSource is { } source)
        {
            _cannedName = source.Name;
            _cannedFolder = source.Folder;
        }
    }

    private async Task SaveCannedAsync(Guid? existingId)
    {
        _saving = true;
        _error = null;

        try
        {
            string name = _cannedName ?? string.Empty;
            string folder = _cannedFolder ?? string.Empty;
            Dictionary<string, PropertyValueType> types = Draft.PropertyTypes();
            if (existingId is { } id)
            {
                await CannedStore.UpdateAsync(id, name, folder, Draft.Body ?? string.Empty, EmptyToNull(Draft.ContentType), Draft.PropertyValues(), types);
            }
            else
            {
                await CannedStore.AddAsync(name, folder, Draft.Body ?? string.Empty, EmptyToNull(Draft.ContentType), Draft.PropertyValues(), types);
            }

            _showCannedSave = false;
            _cannedName = null;
        }
        catch (Exception exception)
        {
            _error = exception.Message;
            Logger.LogWarning(exception, "Saving canned message failed");
        }
        finally
        {
            _saving = false;
        }
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // The picker holds the user's wall-clock time; only the browser knows its zone (the server's
    // TimeZoneInfo.Local is UTC in the container), so the conversion happens client-side.
    private async Task<DateTimeOffset?> ToUtcAsync(DateTime? local)
    {
        if (local is null)
        {
            return null;
        }

        _timeModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>("import", _disposalTokenSource.Token, "./js/time.js");
        string? utc = await _timeModule.InvokeAsync<string?>(
            "localToUtcIso",
            _disposalTokenSource.Token,
            local.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));

        return utc is null
            ? throw new FormatException($"'{local}' is not a valid scheduled enqueue time.")
            : DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    public async ValueTask DisposeAsync()
    {
        await _disposalTokenSource.CancelAsync();
        _disposalTokenSource.Dispose();

        if (_timeModule is not null)
        {
            try
            {
                await _timeModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit is already gone — nothing to clean up on the client.
            }
        }
    }
}
