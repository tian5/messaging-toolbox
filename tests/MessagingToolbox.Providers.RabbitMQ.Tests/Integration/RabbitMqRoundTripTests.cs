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


        entities.Single(entity => entity.Path == "queues/rt-list-quorum").PeekMode.ShouldBe(PeekMode.Requeue);
        entities.Single(entity => entity.Path == "queues/rt-list-stream").PeekMode.ShouldBe(PeekMode.NonDestructive);
        entities.ShouldContain(entity => entity.Path == "exchanges/rt-list-exchange" && entity.Kind == EntityKind.Exchange);
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
