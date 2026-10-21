using Mediator;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Application.Features.Messages.Commands.RepairAndResubmitMessage;

/// <summary>
/// Sends an edited copy of a message. Removing the original is a separate, explicit,
/// confirmed step: only when <see cref="RemoveOriginalAfterSend"/> is set and the original is
/// held by its lock token (received for settlement) — and only after the copy was sent
/// successfully.
/// </summary>
public sealed record RepairAndResubmitMessageCommand(
    Guid ConnectionId,
    string TargetPath,
    string Body,
    string? ContentType,
    IReadOnlyDictionary<string, string> ApplicationProperties,
    string? Subject,
    string? CorrelationId,
    string? SessionId,
    MessageDto? OriginalMessage,
    string? OriginalEntityPath,
    MessageSubQueue OriginalSubQueue,
    string? OriginalLockToken,
    long? OriginalSequenceNumber,
    bool RemoveOriginalAfterSend,
    IReadOnlyDictionary<string, PropertyValueType>? PropertyTypes = null) : IRequest;
