using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Web.Services.Session;

/// <summary>
/// What the composer holds, kept per connection workspace so it survives sends, node switches
/// and the composer being re-created: send, look at the results, come back and send again.
/// Repair &amp; resubmit gets its own draft so it never overwrites the one being composed.
/// </summary>
public sealed class ComposerDraft
{
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>The selected entity the target follows while the user hasn't typed another target.</summary>
    public string? FollowedPath { get; set; }

    public string? Body { get; set; } = string.Empty;

    public string? ContentType { get; set; }

    public List<ComposerPropertyRow> Properties { get; } = [];

    public string? Subject { get; set; }

    public string? MessageId { get; set; }

    public string? CorrelationId { get; set; }

    public string? SessionId { get; set; }

    public string? To { get; set; }

    public string? ReplyTo { get; set; }

    public DateTime? ScheduledLocal { get; set; }

    /// <summary>Routing key for exchange targets; ignored by brokers that route by entity path.</summary>
    public string? RoutingKey { get; set; }

    public int? TimeToLiveSeconds { get; set; }

    public int Copies { get; set; } = 1;

    public bool ShowSystemProperties { get; set; }

    /// <summary>The canned message last inserted; saving can update it instead of adding a new one.</summary>
    public CannedMessageDto? CannedSource { get; set; }

    /// <summary>The prefill already applied, so a re-created composer doesn't apply it over the user's edits.</summary>
    public MessageDto? AppliedPrefill { get; set; }

    /// <summary>
    /// Points the target at the selected entity while the user hasn't chosen another target.
    /// Returns true when the selection moved on.
    /// </summary>
    public bool Follow(string entityPath)
    {
        if (FollowedPath == entityPath)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(TargetPath) || TargetPath == FollowedPath)
        {
            TargetPath = entityPath;
        }

        FollowedPath = entityPath;
        return true;
    }

    /// <summary>Fills the draft from a message (repair) with each property's type taken from its value.</summary>
    public void ApplyMessage(MessageDto message)
    {
        AppliedPrefill = message;
        Body = message.Body;
        ContentType = message.ContentType;
        Subject = message.Subject;
        MessageId = message.MessageId;
        CorrelationId = message.CorrelationId;
        SessionId = message.SessionId;
        To = message.To;
        ReplyTo = message.ReplyTo;
        RoutingKey = message.RoutingKey;
        Properties.Clear();
        foreach (KeyValuePair<string, object?> property in message.ApplicationProperties)
        {
            Properties.Add(new ComposerPropertyRow
            {
                Key = property.Key,
                Value = ApplicationPropertyValues.Format(property.Value),
                Type = ApplicationPropertyValues.TypeOf(property.Value),
            });
        }
    }

    /// <summary>Replaces body, content type and properties with a canned message; system properties stay.</summary>
    public void ApplyCanned(CannedMessageDto canned)
    {
        CannedSource = canned;
        Body = canned.Body;
        ContentType = canned.ContentType;
        Properties.Clear();
        foreach (KeyValuePair<string, string> property in canned.Properties)
        {
            Properties.Add(new ComposerPropertyRow
            {
                Key = property.Key,
                Value = property.Value,
                Type = canned.PropertyTypes?.GetValueOrDefault(property.Key) ?? PropertyValueType.String,
            });
        }
    }

    public Dictionary<string, string> PropertyValues() => Properties
        .Where(row => !string.IsNullOrWhiteSpace(row.Key))
        .GroupBy(row => row.Key.Trim())
        .ToDictionary(group => group.Key, group => group.Last().Value ?? string.Empty);

    public Dictionary<string, PropertyValueType> PropertyTypes() => Properties
        .Where(row => !string.IsNullOrWhiteSpace(row.Key))
        .GroupBy(row => row.Key.Trim())
        .ToDictionary(group => group.Key, group => group.Last().Type);
}

public sealed class ComposerPropertyRow
{
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }

    public PropertyValueType Type { get; set; }
}
