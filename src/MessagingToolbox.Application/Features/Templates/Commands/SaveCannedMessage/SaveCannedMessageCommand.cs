using Mediator;
using MessagingToolbox.Application.Common;

namespace MessagingToolbox.Application.Features.Templates.Commands.SaveCannedMessage;

/// <summary>Saves a reusable message payload with properties into a folder; with an <paramref name="Id"/> it updates (or renames) that message.</summary>
public sealed record SaveCannedMessageCommand(
    Guid? Id,
    string Name,
    string Folder,
    string Body,
    string? ContentType,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyDictionary<string, PropertyValueType>? PropertyTypes = null) : IRequest;
