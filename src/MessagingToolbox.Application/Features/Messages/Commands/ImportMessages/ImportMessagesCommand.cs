using Mediator;

namespace MessagingToolbox.Application.Features.Messages.Commands.ImportMessages;

/// <summary>Imports/replays messages from an exported JSON file to any entity; returns the number sent.</summary>
public sealed record ImportMessagesCommand(
    Guid ConnectionId,
    string TargetPath,
    string Base64Content) : IRequest<int>;
