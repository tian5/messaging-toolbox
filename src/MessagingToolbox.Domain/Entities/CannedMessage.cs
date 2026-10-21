using MessagingToolbox.Domain.Common;

namespace MessagingToolbox.Domain.Entities;

/// <summary>A named, reusable message payload with properties (FR-4.3); organized into folders.</summary>
public class CannedMessage : EntityBase
{
    public string Name { get; set; } = string.Empty;

    public string Folder { get; set; } = AppConstants.DefaultCannedMessageFolder;

    public string Body { get; set; } = string.Empty;

    public string? ContentType { get; set; }

    /// <summary>Application properties serialized as a JSON object.</summary>
    public string PropertiesJson { get; set; } = "{}";
}
