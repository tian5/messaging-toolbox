using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Explorer.Queries.GetEntityTree;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Explorer.Queries.GetEntityTree;

public class GetEntityTreeQueryHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private static readonly ProviderCapabilities FullCapabilities = new(
        CanManageEntities: true,
        CanManageRules: true,
        CanPeek: true,
        CanReceiveSettle: true,
        SupportsSessions: true,
        SupportsScheduling: true,
        SupportsTransactions: true,
        MaxMessageSizeBytes: 256 * 1024);

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly GetEntityTreeQueryHandler _sut;

    public GetEntityTreeQueryHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _provider.Capabilities.Returns(FullCapabilities);
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _sut = new GetEntityTreeQueryHandler(_providerFactory);
    }

    [Fact]
    public async Task Handle_Entities_ReturnsTreeWithSubQueueNodes()
    {
        MessagingEntity queue = new() { Path = "orders", Name = "orders", Kind = EntityKind.Queue };
        MessagingEntity topic = new() { Path = "billing-events", Name = "billing-events", Kind = EntityKind.Topic };
        MessagingEntity subscription = new()
        {
            Path = "billing-events/subscriptions/processor",
            Name = "processor",
            Kind = EntityKind.Subscription,
            ParentPath = "billing-events",
        };

        _provider.ListEntitiesAsync(Arg.Any<CancellationToken>())
            .Returns(TestAsyncEnumerable.Of(queue, topic, subscription));


        IReadOnlyList<EntityTreeNodeDto> tree = await _sut.Handle(new GetEntityTreeQuery(ConnectionId), CancellationToken.None);


        tree.Count.ShouldBe(2);

        EntityTreeNodeDto queueNode = tree.Single(node => node.Kind == EntityKind.Queue);
        queueNode.Path.ShouldBe("orders");
        AssertSubQueueChildren(queueNode);

        EntityTreeNodeDto topicNode = tree.Single(node => node.Kind == EntityKind.Topic);
        topicNode.Children.All(child => !child.IsSubQueueNode).ShouldBeTrue();
        EntityTreeNodeDto subscriptionNode = topicNode.Children.ShouldHaveSingleItem();
        subscriptionNode.Path.ShouldBe("billing-events/subscriptions/processor");
        subscriptionNode.SubQueue.ShouldBe(MessageSubQueue.Active);
        AssertSubQueueChildren(subscriptionNode);
    }

    [Fact]
    public async Task Handle_RuntimeInfo_ReturnsMappedRuntimeInfo()
    {
        MessagingEntity queue = new()
        {
            Path = "orders",
            Name = "orders",
            Kind = EntityKind.Queue,
            Status = MessagingEntityStatus.Active,
            RuntimeInfo = new EntityRuntimeInfo
            {
                ActiveMessageCount = 3,
                ScheduledMessageCount = 1,
                DeadLetterMessageCount = 2,
                SizeInBytes = 4096,
                IsApproximate = true,
            },
        };

        _provider.ListEntitiesAsync(Arg.Any<CancellationToken>())
            .Returns(TestAsyncEnumerable.Of(queue));


        IReadOnlyList<EntityTreeNodeDto> tree = await _sut.Handle(new GetEntityTreeQuery(ConnectionId), CancellationToken.None);


        EntityTreeNodeDto node = tree.ShouldHaveSingleItem();
        node.RuntimeInfo.ShouldNotBeNull();
        node.RuntimeInfo.ActiveMessageCount.ShouldBe(3);
        node.RuntimeInfo.ScheduledMessageCount.ShouldBe(1);
        node.RuntimeInfo.DeadLetterMessageCount.ShouldBe(2);
        node.RuntimeInfo.SizeInBytes.ShouldBe(4096);
        node.RuntimeInfo.IsApproximate.ShouldBeTrue();

        node.Children.Count.ShouldBe(3);
        foreach (EntityTreeNodeDto child in node.Children)
        {
            child.RuntimeInfo.ShouldNotBeNull();
            child.RuntimeInfo!.ActiveMessageCount.ShouldBe(3);
            child.RuntimeInfo.ScheduledMessageCount.ShouldBe(1);
            child.RuntimeInfo.DeadLetterMessageCount.ShouldBe(2);
            child.RuntimeInfo.IsApproximate.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Handle_SchedulingUnsupported_OmitsScheduledNode()
    {
        _provider.Capabilities.Returns(FullCapabilities with { SupportsScheduling = false });

        MessagingEntity queue = new() { Path = "orders", Name = "orders", Kind = EntityKind.Queue };
        _provider.ListEntitiesAsync(Arg.Any<CancellationToken>())
            .Returns(TestAsyncEnumerable.Of(queue));


        IReadOnlyList<EntityTreeNodeDto> tree = await _sut.Handle(new GetEntityTreeQuery(ConnectionId), CancellationToken.None);


        EntityTreeNodeDto node = tree.ShouldHaveSingleItem();
        node.Children.Count.ShouldBe(2);
        node.Children.ShouldNotContain(child => child.SubQueue == MessageSubQueue.Scheduled);
    }

    private static void AssertSubQueueChildren(EntityTreeNodeDto entityNode)
    {
        entityNode.Children.Count.ShouldBe(3);
        entityNode.Children.All(child => child.IsSubQueueNode && child.Path == entityNode.Path).ShouldBeTrue();
        entityNode.Children[0].SubQueue.ShouldBe(MessageSubQueue.Active);
        entityNode.Children[1].SubQueue.ShouldBe(MessageSubQueue.Scheduled);
        entityNode.Children[2].SubQueue.ShouldBe(MessageSubQueue.DeadLetter);
    }
}
