using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Models;

namespace MessagingToolbox.Application.Common.Dtos;

/// <summary>
/// A connection <see cref="Capabilities"/> is null when the provider cannot
/// be resolved. <see cref="Source"/> indicates whether the connection is host-seeded
/// (config) or user-created and persisted (saved).
/// </summary>
public sealed record ConnectionDto(
    Guid Id,
    string Name,
    string DisplayName,
    ProviderKind Kind,
    ConnectionSource Source,
    ProviderCapabilities? Capabilities);
