using Mediator;

namespace MessagingToolbox.Application.Features.Management.Notifications;

public sealed record EntityListChangedNotification(Guid ConnectionId) : INotification;
