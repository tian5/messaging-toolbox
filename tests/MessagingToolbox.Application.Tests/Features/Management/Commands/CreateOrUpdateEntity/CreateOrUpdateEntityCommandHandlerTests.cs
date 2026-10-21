using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Management.Commands.CreateOrUpdateEntity;
using MessagingToolbox.Application.Features.Management.Notifications;
using MessagingToolbox.Application.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Management.Commands.CreateOrUpdateEntity;

public class CreateOrUpdateEntityCommandHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly IAuditLog _auditLog;
    private readonly IPublisher _publisher;
    private readonly CreateOrUpdateEntityCommandHandler _sut;

    public CreateOrUpdateEntityCommandHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _auditLog = Substitute.For<IAuditLog>();
        _publisher = Substitute.For<IPublisher>();
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _sut = new CreateOrUpdateEntityCommandHandler(
            _providerFactory,
            Substitute.For<IConnectionCatalog>(),
            _auditLog,
            _publisher,
            Substitute.For<ILogger<CreateOrUpdateEntityCommandHandler>>());
    }

    [Fact]
    public async Task Handle_QueueDefinition_CreatesAndReturnsPath()
    {
        _provider.CreateOrUpdateEntityAsync(
                Arg.Is<EntityDefinition>(definition =>
                    definition.Path == "orders" &&
                    definition.Kind == EntityKind.Queue),
                Arg.Any<CancellationToken>())
            .Returns(new MessagingEntity { Path = "orders", Name = "orders", Kind = EntityKind.Queue });


        string path = await _sut.Handle(
            new CreateOrUpdateEntityCommand(ConnectionId, "orders", EntityKind.Queue, null, new Dictionary<string, object?> { ["RequiresSession"] = true }),
            CancellationToken.None);


        path.ShouldBe("orders");
        await _auditLog.Received(1).WriteAsync(AuditLog.SaveEntity, Arg.Any<string>(), "orders", MessageSubQueue.Active, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _publisher.Received(1).Publish(
            Arg.Is<EntityListChangedNotification>(notification => notification.ConnectionId == ConnectionId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SubscriptionDefinition_BuildsSubscriptionPath()
    {
        _provider.CreateOrUpdateEntityAsync(
                Arg.Is<EntityDefinition>(definition =>
                    definition.Path == "events/subscriptions/processor" &&
                    definition.Kind == EntityKind.Subscription &&
                    definition.ParentPath == "events"),
                Arg.Any<CancellationToken>())
            .Returns(new MessagingEntity { Path = "events/subscriptions/processor", Name = "processor", Kind = EntityKind.Subscription, ParentPath = "events" });


        string path = await _sut.Handle(
            new CreateOrUpdateEntityCommand(ConnectionId, "events/subscriptions/processor", EntityKind.Subscription, "events", new Dictionary<string, object?>()),
            CancellationToken.None);


        path.ShouldBe("events/subscriptions/processor");
    }
}
