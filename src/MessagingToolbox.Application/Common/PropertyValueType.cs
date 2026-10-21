namespace MessagingToolbox.Application.Common;

/// <summary>The value type an application property is sent with (FR-4.1).</summary>
public enum PropertyValueType
{
    String,
    Int32,
    Int64,
    Boolean,
    Double,

    /// <summary>A UTC timestamp, sent as a <see cref="DateTimeOffset"/>.</summary>
    DateTime,
    Guid,
}
