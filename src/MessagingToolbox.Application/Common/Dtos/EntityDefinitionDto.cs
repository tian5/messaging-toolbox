using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Common.Dtos;

/// <summary>Broker-agnostic entity definition for management create/edit/delete operations (ADR 21).</summary>
public sealed record EntityDefinitionDto(
    string Name,
    EntityKind Kind,
    string? ParentPath,
    IReadOnlyDictionary<string, object?> Options);
