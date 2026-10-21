using Mediator;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Connections.Commands.SaveConnection;

/// <summary>Creates or updates a saved connection (FR-1.1); the connection string is protected before storage.</summary>
public sealed record SaveConnectionCommand(Guid? Id, string DisplayName, string ConnectionString, ProviderKind Kind) : IRequest;
