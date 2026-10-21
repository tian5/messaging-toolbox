using Mediator;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Messages.Queries.GetPurgePreview;

/// <summary>Count preview before a purge; null when the connection cannot report counts.</summary>
public sealed record GetPurgePreviewQuery(
    Guid ConnectionId,
    string EntityPath,
    MessageSubQueue SubQueue) : IRequest<long?>;
