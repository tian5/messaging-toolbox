using System.Text;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using RabbitMQ.Client;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.RabbitMQ.Tests.Integration;

/// <summary>
/// Round-trips against a real broker: what the requeue-peek (ADR 18), publish routing and the
/// management-based discovery rely on.
/// </summary>
[Trait("Category", "Emulator")]
public class RabbitMqRoundTripTests : IClassFixture<RabbitMqFixture>
{
    private readonly RabbitMqFixture _fixture;

    public RabbitMqRoundTripTests(RabbitMqFixture fixture)
    {
        _fixture = fixture;
    }

    [BrokerFact]
    public async Task ListEntitiesAsync_ManagementAvailable_ReportsQueueTypesAndUserExchanges()
    {
        await _fixture.DeclareAsync(async channel =>
        {
            await channel.QueueDeclareAsync("rt-list-quorum", true, false, false, QueueType("quorum"));
            await channel.QueueDeclareAsync("rt-list-stream", true, false, false, QueueType("stream"));
            await channel.ExchangeDeclareAsync("rt-list-exchange", ExchangeType.Topic, durable: true);
        });
        await using RabbitMqProvider sut = _fixture.CreateProvider();


        List<MessagingEntity> entities = await ListAsync(sut);


        MessagingEntity quorum = entities.Single(entity => entity.Path == "queues/rt-list-quorum");
        quorum.PeekMode.ShouldBe(PeekMode.Requeue);
        quorum.TypeName.ShouldBe("quorum");
        quorum.Properties.ShouldContain(new EntityProperty("Settings", "Durable", "yes"));
        MessagingEntity stream = entities.Single(entity => entity.Path == "queues/rt-list-stream");
        stream.PeekMode.ShouldBe(PeekMode.NonDestructive);
        stream.TypeName.ShouldBe("stream");
        entities.ShouldContain(entity => entity.Path == "exchanges/rt-list-exchange" && entity.Kind == EntityKind.Exchange && entity.TypeName == "topic");
        entities.ShouldNotContain(entity => entity.Name.StartsWith("amq.", StringComparison.Ordinal));
    }

    [BrokerFact]
    public async Task PeekAsync_QuorumQueue_RequeuesEverythingWithoutBumpingDeliveryCount()
    {
        await _fixture.DeclareAsync(async channel =>
        {
            await channel.QueueDeclareAsync("rt-peek-quorum", true, false, false, QueueType("quorum"));
            await PublishAsync(channel, "rt-peek-quorum", "one", "two", "three");
        });
        await using RabbitMqProvider sut = _fixture.CreateProvider();


        IReadOnlyList<ToolboxMessage> first = await sut.PeekAsync(new PeekRequest("queues/rt-peek-quorum", MessageSubQueue.Active, 10, null), CancellationToken.None);
        IReadOnlyList<ToolboxMessage> second = await sut.PeekAsync(new PeekRequest("queues/rt-peek-quorum", MessageSubQueue.Active, 10, null), CancellationToken.None);


        first.Select(Body).ShouldBe(["one", "two", "three"]);
        second.Select(Body).ShouldBe(["one", "two", "three"]);
        second.ShouldAllBe(message => message.DeliveryCount <= 1);
        (await sut.GetRuntimeInfoAsync("queues/rt-peek-quorum", CancellationToken.None)).ActiveMessageCount.ShouldBe(3);
    }

    [BrokerFact]
    public async Task PeekAsync_MaxBelowQueueLength_ReturnsHeadAndKeepsTheRest()
    {
        await _fixture.DeclareAsync(async channel =>
        {
            await channel.QueueDeclareAsync("rt-peek-partial", true, false, false);
            await PublishAsync(channel, "rt-peek-partial", "a", "b", "c");
        });
        await using RabbitMqProvider sut = _fixture.CreateProvider();


        IReadOnlyList<ToolboxMessage> peeked = await sut.PeekAsync(new PeekRequest("queues/rt-peek-partial", MessageSubQueue.Active, 2, null), CancellationToken.None);
        IReadOnlyList<ToolboxMessage> received = await sut.ReceiveAsync(new ReceiveRequest("queues/rt-peek-partial", MessageSubQueue.Active, 10, null), CancellationToken.None);


        peeked.Select(Body).ShouldBe(["a", "b"]);
        received.Select(Body).ShouldBe(["a", "b", "c"]);
    }

    [BrokerFact]
    public async Task PeekAsync_Stream_ReadsFromFirstOffsetWithoutConsuming()
    {
        await _fixture.DeclareAsync(async channel =>
        {
            await channel.QueueDeclareAsync("rt-stream", true, false, false, QueueType("stream"));
            await PublishAsync(channel, "rt-stream", "s1", "s2");
        });
        await using RabbitMqProvider sut = _fixture.CreateProvider();
        await ListAsync(sut);


        IReadOnlyList<ToolboxMessage> first = await sut.PeekAsync(new PeekRequest("queues/rt-stream", MessageSubQueue.Active, 10, null), CancellationToken.None);
        IReadOnlyList<ToolboxMessage> second = await sut.PeekAsync(new PeekRequest("queues/rt-stream", MessageSubQueue.Active, 10, null), CancellationToken.None);


        first.Select(Body).ShouldBe(["s1", "s2"]);
        second.Select(Body).ShouldBe(["s1", "s2"]);
        first[0].SequenceNumber.ShouldBe(0);
    }

    [BrokerFact]
    public async Task SendAsync_TopicExchangeWithRoutingKey_RoutesToMatchingQueuesOnly()
    {
        await _fixture.DeclareAsync(async channel =>
        {
            await channel.ExchangeDeclareAsync("rt-events", ExchangeType.Topic, durable: true);
            await channel.QueueDeclareAsync("rt-billing", true, false, false);
            await channel.QueueDeclareAsync("rt-shipping", true, false, false);
            await channel.QueueBindAsync("rt-billing", "rt-events", "billing.#");
            await channel.QueueBindAsync("rt-shipping", "rt-events", "shipping.#");
        });
        await using RabbitMqProvider sut = _fixture.CreateProvider();


        await sut.SendAsync("exchanges/rt-events", [new ToolboxMessage { Body = "{}"u8.ToArray(), RoutingKey = "billing.created", Subject = "InvoiceCreated" }], CancellationToken.None);


        IReadOnlyList<ToolboxMessage> billing = await sut.ReceiveAsync(new ReceiveRequest("queues/rt-billing", MessageSubQueue.Active, 10, null), CancellationToken.None);
        IReadOnlyList<ToolboxMessage> shipping = await sut.ReceiveAsync(new ReceiveRequest("queues/rt-shipping", MessageSubQueue.Active, 10, null), CancellationToken.None);
        ToolboxMessage message = billing.ShouldHaveSingleItem();
        message.RoutingKey.ShouldBe("billing.created");
        message.Subject.ShouldBe("InvoiceCreated");
        shipping.ShouldBeEmpty();
    }

    [BrokerFact]
    public async Task SendAsync_UnboundRoutingKey_ThrowsNotRouted()
    {
        await _fixture.DeclareAsync(channel => channel.ExchangeDeclareAsync("rt-unbound", ExchangeType.Topic, durable: true));
        await using RabbitMqProvider sut = _fixture.CreateProvider();


        MessagingProviderException exception = await Should.ThrowAsync<MessagingProviderException>(
            () => sut.SendAsync("exchanges/rt-unbound", [new ToolboxMessage { Body = "x"u8.ToArray(), RoutingKey = "nobody.listens" }], CancellationToken.None));


        exception.Message.ShouldContain("not routed");
    }

    [BrokerFact]
    public async Task SendAsync_MissingQueue_ThrowsEntityNotFound()
    {
        await using RabbitMqProvider sut = _fixture.CreateProvider();


        await Should.ThrowAsync<MessagingEntityNotFoundException>(
            () => sut.SendAsync("queues/rt-does-not-exist", [new ToolboxMessage { Body = "x"u8.ToArray() }], CancellationToken.None));
    }

    [BrokerFact]
    public async Task PurgeAsync_ClassicQueue_RemovesReadyMessagesAndReturnsCount()
    {
        await _fixture.DeclareAsync(async channel =>
        {
            await channel.QueueDeclareAsync("rt-purge", true, false, false);
            await PublishAsync(channel, "rt-purge", "a", "b");
        });
        await using RabbitMqProvider sut = _fixture.CreateProvider();


        long purged = await sut.PurgeAsync("queues/rt-purge", MessageSubQueue.Active, CancellationToken.None);


        purged.ShouldBe(2);
        (await sut.PeekAsync(new PeekRequest("queues/rt-purge", MessageSubQueue.Active, 10, null), CancellationToken.None)).ShouldBeEmpty();
    }

    [BrokerFact]
    public async Task ListEntitiesAsync_ManagementUnreachable_CountsConfiguredQueuesOverAmqp()
    {
        await _fixture.DeclareAsync(async channel =>
        {
            await channel.QueueDeclareAsync("rt-configured", true, false, false);
            await PublishAsync(channel, "rt-configured", "x");
        });
        await using RabbitMqProvider sut = _fixture.CreateProvider(["rt-configured"], managementEndpoint: "http://127.0.0.1:9");


        List<MessagingEntity> entities = await ListAsync(sut);


        sut.Capabilities.ManagementUnavailableReason.ShouldNotBeNull();
        entities.ShouldHaveSingleItem().RuntimeInfo!.ActiveMessageCount.ShouldBe(1);
    }

    [BrokerFact]
    public async Task SettleAsync_CompleteAndAbandon_RemovesOneAndRequeuesTheOtherWithoutBumpingDeliveryCount()
    {
        await _fixture.DeclareAsync(async channel =>
        {
            await channel.QueueDeclareAsync("rt-settle", true, false, false, QueueType("quorum"));
            await PublishAsync(channel, "rt-settle", "keep", "remove");
        });
        await using RabbitMqProvider sut = _fixture.CreateProvider();
        IReadOnlyList<ToolboxMessage> held = await sut.ReceiveForSettlementAsync("queues/rt-settle", MessageSubQueue.Active, 10, null, CancellationToken.None);


        await sut.SettleAsync(new SettleRequest("queues/rt-settle", MessageSubQueue.Active, SettlementOperation.Abandon, [held[0].LockToken!], null, null), CancellationToken.None);
        await sut.SettleAsync(new SettleRequest("queues/rt-settle", MessageSubQueue.Active, SettlementOperation.Complete, [held[1].LockToken!], null, null), CancellationToken.None);


        held.Select(Body).ShouldBe(["keep", "remove"]);
        IReadOnlyList<ToolboxMessage> remaining = await sut.ReceiveAsync(new ReceiveRequest("queues/rt-settle", MessageSubQueue.Active, 10, null), CancellationToken.None);
        ToolboxMessage kept = remaining.ShouldHaveSingleItem();
        Body(kept).ShouldBe("keep");
        kept.DeliveryCount.ShouldBeLessThanOrEqualTo(1);
    }

    [BrokerFact]
    public async Task SettleAsync_DeadLetter_RoutesThroughTheDeadLetterExchangeWithDeathHistory()
    {
        await _fixture.DeclareAsync(async channel =>
        {
            await channel.ExchangeDeclareAsync("rt-dlx", ExchangeType.Fanout, durable: true);
            await channel.QueueDeclareAsync("rt-dlx-target", true, false, false);
            await channel.QueueBindAsync("rt-dlx-target", "rt-dlx", string.Empty);
            await channel.QueueDeclareAsync("rt-dlx-source", true, false, false, new Dictionary<string, object?> { ["x-dead-letter-exchange"] = "rt-dlx" });
            await PublishAsync(channel, "rt-dlx-source", "poison");
        });
        await using RabbitMqProvider sut = _fixture.CreateProvider();
        MessagingEntity source = (await ListAsync(sut)).Single(entity => entity.Path == "queues/rt-dlx-source");
        IReadOnlyList<ToolboxMessage> held = await sut.ReceiveForSettlementAsync("queues/rt-dlx-source", MessageSubQueue.Active, 10, null, CancellationToken.None);


        await sut.SettleAsync(new SettleRequest("queues/rt-dlx-source", MessageSubQueue.Active, SettlementOperation.DeadLetter, [held[0].LockToken!], "ignored", null), CancellationToken.None);


        source.DeadLetterExchange.ShouldBe("rt-dlx");
        IReadOnlyList<ToolboxMessage> deadLettered = await sut.PeekAsync(new PeekRequest("queues/rt-dlx-target", MessageSubQueue.Active, 10, null), CancellationToken.None);
        ToolboxMessage message = deadLettered.ShouldHaveSingleItem();
        message.DeadLetterReason.ShouldBe("rejected");
        message.DeadLetterSourcePath.ShouldBe("queues/rt-dlx-source");
        (await sut.GetRuntimeInfoAsync("queues/rt-dlx-source", CancellationToken.None)).ActiveMessageCount.ShouldBe(0);
    }

    [BrokerFact]
    public async Task SettleAsync_DeadLetterWithoutDeadLetterExchange_RefusesAndKeepsTheMessage()
    {
        await _fixture.DeclareAsync(async channel =>
        {
            await channel.QueueDeclareAsync("rt-no-dlx", true, false, false);
            await PublishAsync(channel, "rt-no-dlx", "precious");
        });
        await using RabbitMqProvider sut = _fixture.CreateProvider();
        await ListAsync(sut);
        IReadOnlyList<ToolboxMessage> held = await sut.ReceiveForSettlementAsync("queues/rt-no-dlx", MessageSubQueue.Active, 10, null, CancellationToken.None);


        await Should.ThrowAsync<NotSupportedByProviderException>(() => sut.SettleAsync(
            new SettleRequest("queues/rt-no-dlx", MessageSubQueue.Active, SettlementOperation.DeadLetter, [held[0].LockToken!], null, null),
            CancellationToken.None));


        await sut.SettleAsync(new SettleRequest("queues/rt-no-dlx", MessageSubQueue.Active, SettlementOperation.Abandon, [held[0].LockToken!], null, null), CancellationToken.None);
        (await sut.GetRuntimeInfoAsync("queues/rt-no-dlx", CancellationToken.None)).ActiveMessageCount.ShouldBe(1);
    }

    [BrokerFact]
    public async Task DisposeAsync_WithHeldDeliveries_RequeuesThem()
    {
        await _fixture.DeclareAsync(async channel =>
        {
            await channel.QueueDeclareAsync("rt-held-dispose", true, false, false);
            await PublishAsync(channel, "rt-held-dispose", "a", "b");
        });
        RabbitMqProvider sut = _fixture.CreateProvider();
        IReadOnlyList<ToolboxMessage> held = await sut.ReceiveForSettlementAsync("queues/rt-held-dispose", MessageSubQueue.Active, 10, null, CancellationToken.None);


        await sut.DisposeAsync();


        held.Count.ShouldBe(2);
        await using RabbitMqProvider other = _fixture.CreateProvider();
        (await other.GetRuntimeInfoAsync("queues/rt-held-dispose", CancellationToken.None)).ActiveMessageCount.ShouldBe(2);
    }

    [BrokerFact]
    public async Task PrepareResubmitAsync_FromDeadLetterQueue_SendsCloneBackAndRemovesOriginal()
    {
        await _fixture.DeclareAsync(async channel =>
        {
            await channel.QueueDeclareAsync("rt-resubmit-source", true, false, false);
            await channel.QueueDeclareAsync("rt-resubmit-dlq", true, false, false);
            await PublishAsync(channel, "rt-resubmit-dlq", "retry-me");
        });
        await using RabbitMqProvider sut = _fixture.CreateProvider();
        ToolboxMessage original = (await sut.ReceiveForSettlementAsync("queues/rt-resubmit-dlq", MessageSubQueue.Active, 10, null, CancellationToken.None)).Single();


        ToolboxMessage clone = await sut.PrepareResubmitAsync("queues/rt-resubmit-dlq", MessageSubQueue.Active, original, ResubmitOverrides.Empty, CancellationToken.None);
        await sut.SendAsync("queues/rt-resubmit-source", [clone], CancellationToken.None);
        await sut.SettleAsync(new SettleRequest("queues/rt-resubmit-dlq", MessageSubQueue.Active, SettlementOperation.Complete, [original.LockToken!], null, null), CancellationToken.None);


        (await sut.GetRuntimeInfoAsync("queues/rt-resubmit-dlq", CancellationToken.None)).ActiveMessageCount.ShouldBe(0);
        ToolboxMessage resent = (await sut.ReceiveAsync(new ReceiveRequest("queues/rt-resubmit-source", MessageSubQueue.Active, 10, null), CancellationToken.None)).Single();
        Body(resent).ShouldBe("retry-me");
        resent.ApplicationProperties.ShouldContainKey("toolbox-replayed-from");
    }

    [BrokerFact]
    public async Task CreateOrUpdateEntityAsync_QuorumQueue_IsListedReadBackAndRefusesAChangedType()
    {
        await using RabbitMqProvider sut = _fixture.CreateProvider();
        Dictionary<string, object?> options = new()
        {
            ["x-queue-type"] = "quorum",
            ["durable"] = true,
            ["x-message-ttl"] = TimeSpan.FromMinutes(5),
            ["x-delivery-limit"] = 3L,
        };


        await sut.CreateOrUpdateEntityAsync(new EntityDefinition("rt-manage-quorum", EntityKind.Queue, null, options), CancellationToken.None);
        EntityDefinition definition = await sut.GetEntityDefinitionAsync("queues/rt-manage-quorum", CancellationToken.None);
        MessagingProviderException refused = await Should.ThrowAsync<MessagingProviderException>(() => sut.CreateOrUpdateEntityAsync(
            new EntityDefinition("rt-manage-quorum", EntityKind.Queue, null, new Dictionary<string, object?> { ["x-queue-type"] = "classic" }), CancellationToken.None));


        (await ListAsync(sut)).Single(entity => entity.Path == "queues/rt-manage-quorum").TypeName.ShouldBe("quorum");
        definition.Options["x-message-ttl"].ShouldBe(TimeSpan.FromMinutes(5));
        definition.Options["x-delivery-limit"].ShouldBe(3L);
        refused.Message.ShouldContain("inequivalent");
    }

    [BrokerFact]
    public async Task DeleteEntityAsync_CreatedExchange_IsGoneFromTheListing()
    {
        await using RabbitMqProvider sut = _fixture.CreateProvider();
        await sut.CreateOrUpdateEntityAsync(
            new EntityDefinition("rt-manage-exchange", EntityKind.Exchange, null, new Dictionary<string, object?> { ["type"] = "fanout" }), CancellationToken.None);
        (await ListAsync(sut)).ShouldContain(entity => entity.Path == "exchanges/rt-manage-exchange" && entity.TypeName == "fanout");


        await sut.DeleteEntityAsync("exchanges/rt-manage-exchange", CancellationToken.None);


        (await ListAsync(sut)).ShouldNotContain(entity => entity.Path == "exchanges/rt-manage-exchange");
    }

    [BrokerFact]
    public async Task CreateRoutingRuleAsync_TopicBinding_RoutesMatchingMessagesUntilDeleted()
    {
        await _fixture.DeclareAsync(async channel =>
        {
            await channel.QueueDeclareAsync("rt-bind-queue", true, false, false);
            await channel.ExchangeDeclareAsync("rt-bind-exchange", ExchangeType.Topic, durable: true);
        });
        await using RabbitMqProvider sut = _fixture.CreateProvider();


        RoutingRule created = await sut.CreateRoutingRuleAsync(
            "exchanges/rt-bind-exchange", "queues/rt-bind-queue", new Dictionary<string, object?> { ["routing-key"] = "invoice.*" }, CancellationToken.None);
        await sut.SendAsync("exchanges/rt-bind-exchange", [Message("routed", "invoice.paid")], CancellationToken.None);
        MessagingProviderException unroutable = await Should.ThrowAsync<MessagingProviderException>(
            () => sut.SendAsync("exchanges/rt-bind-exchange", [Message("unrouted", "order.paid")], CancellationToken.None));
        RoutingRule listed = (await sut.ListRoutingRulesAsync("queues/rt-bind-queue", CancellationToken.None)).ShouldHaveSingleItem();
        await sut.DeleteRoutingRuleAsync(listed, CancellationToken.None);


        unroutable.Message.ShouldContain("not routed");
        listed.Id.ShouldBe(created.Id);
        listed.SourcePath.ShouldBe("exchanges/rt-bind-exchange");
        listed.Options["routing-key"].ShouldBe("invoice.*");
        (await sut.ListRoutingRulesAsync("queues/rt-bind-queue", CancellationToken.None)).ShouldBeEmpty();
        Body((await sut.ReceiveAsync(new ReceiveRequest("queues/rt-bind-queue", MessageSubQueue.Active, 10, null), CancellationToken.None)).ShouldHaveSingleItem()).ShouldBe("routed");
    }

    [BrokerFact]
    public async Task MoveMessagesAsync_ToQuorumQueueWithDeadLetterExchange_MovesEverythingInOrderWithHeaders()
    {
        await _fixture.DeclareAsync(async channel =>
        {
            await channel.QueueDeclareAsync("rt-move-src", true, false, false);
            await channel.QueueDeclareAsync("rt-move-dst", true, false, false, new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-dead-letter-exchange"] = "rt-move-dlx",
            });
            BasicProperties properties = new() { Headers = new Dictionary<string, object?> { ["tenant"] = "acme" } };
            await channel.BasicPublishAsync(string.Empty, "rt-move-src", false, properties, Encoding.UTF8.GetBytes("first"));
            await PublishAsync(channel, "rt-move-src", "second", "third");
        });
        await using RabbitMqProvider sut = _fixture.CreateProvider();
        await sut.ProbeCapabilitiesAsync(CancellationToken.None);


        long moved = await sut.MoveMessagesAsync("queues/rt-move-src", "queues/rt-move-dst", CancellationToken.None);


        sut.Capabilities.CanMoveMessages.ShouldBeTrue();
        moved.ShouldBe(3);
        (await sut.GetRuntimeInfoAsync("queues/rt-move-src", CancellationToken.None)).ActiveMessageCount.ShouldBe(0);
        IReadOnlyList<ToolboxMessage> arrived = await sut.PeekAsync(new PeekRequest("queues/rt-move-dst", MessageSubQueue.Active, 10, null), CancellationToken.None);
        arrived.Select(Body).ShouldBe(["first", "second", "third"]);
        arrived[0].ApplicationProperties["tenant"].ShouldBe("acme");
    }

    [BrokerFact]
    public async Task MoveMessagesAsync_MissingDestination_ThrowsEntityNotFoundAndKeepsTheMessages()
    {
        await _fixture.DeclareAsync(async channel =>
        {
            await channel.QueueDeclareAsync("rt-move-orphan", true, false, false);
            await PublishAsync(channel, "rt-move-orphan", "a");
        });
        await using RabbitMqProvider sut = _fixture.CreateProvider();


        await Should.ThrowAsync<MessagingEntityNotFoundException>(
            () => sut.MoveMessagesAsync("queues/rt-move-orphan", "queues/rt-move-nowhere", CancellationToken.None));


        (await sut.GetRuntimeInfoAsync("queues/rt-move-orphan", CancellationToken.None)).ActiveMessageCount.ShouldBe(1);
    }

    [BrokerFact]
    public async Task MoveMessagesAsync_FromStream_ThrowsNotSupported()
    {
        await _fixture.DeclareAsync(async channel =>
        {
            await channel.QueueDeclareAsync("rt-move-stream", true, false, false, QueueType("stream"));
            await channel.QueueDeclareAsync("rt-move-from-stream", true, false, false);
        });
        await using RabbitMqProvider sut = _fixture.CreateProvider();


        await Should.ThrowAsync<NotSupportedByProviderException>(
            () => sut.MoveMessagesAsync("queues/rt-move-stream", "queues/rt-move-from-stream", CancellationToken.None));
    }

    private static ToolboxMessage Message(string body, string routingKey) => new()
    {
        Body = Encoding.UTF8.GetBytes(body),
        RoutingKey = routingKey,
    };

    private static Dictionary<string, object?> QueueType(string type) => new() { ["x-queue-type"] = type };

    private static string Body(ToolboxMessage message) => Encoding.UTF8.GetString(message.Body);

    private static async Task PublishAsync(IChannel channel, string queue, params string[] bodies)
    {
        foreach (string body in bodies)
        {
            await channel.BasicPublishAsync(string.Empty, queue, Encoding.UTF8.GetBytes(body));
        }
    }

    private static async Task<List<MessagingEntity>> ListAsync(RabbitMqProvider sut)
    {
        List<MessagingEntity> entities = [];
        await foreach (MessagingEntity entity in sut.ListEntitiesAsync(CancellationToken.None))
        {
            entities.Add(entity);
        }

        return entities;
    }
}
