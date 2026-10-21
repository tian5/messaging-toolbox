using Mediator;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;

namespace MessagingToolbox.Application.Features.Messages.Commands.ResendMessage;

/// <summary>Sends an exact copy of an existing message to a chosen target; never touches the original.</summary>
public sealed record ResendMessageCommand(
    Guid ConnectionId,
    string TargetPath,
    string OriginalEntityPath,
    MessageSubQueue OriginalSubQueue,
    MessageDto Message) : IRequest;
