using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Common.Dtos;

/// <summary>Broker-agnostic entity definition for management create/edit/delete operations.</summary>
public sealed record EntityDefinitionDto(
    string Path,
    EntityKind Kind,
    string? ParentPath,
    IReadOnlyDictionary<string, object?> Options);
