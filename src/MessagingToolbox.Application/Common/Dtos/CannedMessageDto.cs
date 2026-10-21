using MessagingToolbox.Application.Common;

namespace MessagingToolbox.Application.Common.Dtos;

/// <summary>A named, reusable message payload with properties (FR-4.3).</summary>
public sealed record CannedMessageDto(
    Guid Id,
    string Name,
    string Folder,
    string Body,
    string? ContentType,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyDictionary<string, PropertyValueType>? PropertyTypes = null);
