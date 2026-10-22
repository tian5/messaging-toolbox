using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Providers.RabbitMQ.Management;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Providers.RabbitMQ.Tests;

public class RabbitMqProviderTests
{
    private readonly IRabbitMqManagementClient _management = Substitute.For<IRabbitMqManagementClient>();
    private readonly FakeTimeProvider _timeProvider = new();

    private RabbitMqProvider CreateProvider(IReadOnlyList<string>? configuredQueues = null) => new(
        RabbitMqConnectionSettings.Parse("amqp://guest:guest@localhost:1", null),
        _management,
        configuredQueues ?? [],
        NullLogger<RabbitMqProvider>.Instance,
        _timeProvider);

    [Fact]
    public async Task ListEntitiesAsync_ManagementAvailable_ListsQueuesAndUserExchangesWithPeekModes()
    {
        _management.ListQueuesAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new RabbitMqQueueInfo
            {
                Name = "orders",
                Type = "quorum",
                Messages = 3,
                Arguments = new() { ["x-dead-letter-exchange"] = System.Text.Json.JsonDocument.Parse("\"orders.dlx\"").RootElement },
            },
            new RabbitMqQueueInfo { Name = "activity", Type = "stream", MessagesReady = 2, MessagesUnacknowledged = 1 },
        ]);
        _management.ListExchangesAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new RabbitMqExchangeInfo { Name = string.Empty, Type = "direct" },
            new RabbitMqExchangeInfo { Name = "amq.topic", Type = "topic" },
            new RabbitMqExchangeInfo { Name = "internal-x", Type = "topic", Internal = true },
            new RabbitMqExchangeInfo { Name = "events", Type = "topic" },
        ]);
        await using RabbitMqProvider sut = CreateProvider();


        List<MessagingEntity> entities = await ListAsync(sut);


        sut.Capabilities.ManagementUnavailableReason.ShouldBeNull();
        entities.Select(entity => entity.Path).ShouldBe(["queues/orders", "queues/activity", "exchanges/events"]);
        entities[0].PeekMode.ShouldBe(PeekMode.Requeue);
        entities[0].RuntimeInfo!.ActiveMessageCount.ShouldBe(3);
        entities[0].DeadLetterExchange.ShouldBe("orders.dlx");
        entities[1].DeadLetterExchange.ShouldBeNull();
        entities[1].PeekMode.ShouldBe(PeekMode.NonDestructive);
        entities[1].RuntimeInfo!.ActiveMessageCount.ShouldBe(3);
        entities[2].Kind.ShouldBe(EntityKind.Exchange);
        entities.Select(entity => entity.TypeName).ShouldBe(["quorum", "stream", "topic"]);
        entities[0].Properties.ShouldContain(property => property.Key == "x-dead-letter-exchange" && property.Value == "orders.dlx");
        entities[0].Operations.ShouldBe(EntityOperations.All);
        entities[1].Operations.ShouldSatisfyAllConditions(
            operations => operations.CanReceive.ShouldBeFalse(),
            operations => operations.CanReceiveSettle.ShouldBeFalse(),
            operations => operations.CanPurge.ShouldBeFalse(),
            operations => operations.Reason.ShouldNotBeNull());
    }

    [Fact]
    public async Task ListEntitiesAsync_ManagementUnreachable_FallsBackToConfiguredQueuesAndExplainsWhy()
    {
        _management.ProbeAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("Connection refused"));
        await using RabbitMqProvider sut = CreateProvider(["orders"]);


        List<MessagingEntity> entities = await ListAsync(sut);


        sut.Capabilities.ManagementUnavailableReason!.ShouldContain("management API is unavailable");
        MessagingEntity queue = entities.ShouldHaveSingleItem();
        queue.Path.ShouldBe("queues/orders");
        queue.PeekMode.ShouldBe(PeekMode.Requeue);
        queue.RuntimeInfo.ShouldBeNull();
        await _management.DidNotReceiveWithAnyArgs().ListQueuesAsync(default);
    }

    [Fact]
    public async Task ListEntitiesAsync_ProbeFailedEarlier_ReprobesAfterInterval()
    {
        _management.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(_ => throw new HttpRequestException("Connection refused"), _ => Task.CompletedTask);
        _management.ListQueuesAsync(Arg.Any<CancellationToken>()).Returns([]);
        _management.ListExchangesAsync(Arg.Any<CancellationToken>()).Returns([]);
        await using RabbitMqProvider sut = CreateProvider();
        await sut.ProbeCapabilitiesAsync(CancellationToken.None);


        _timeProvider.Advance(RabbitMqProvider.ManagementReprobeInterval);
        await ListAsync(sut);


        sut.Capabilities.ManagementUnavailableReason.ShouldBeNull();
        await _management.Received(2).ProbeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Capabilities_Always_ReportSettlementWithoutSubQueuesSequencesOrExpiry()
    {
        await using RabbitMqProvider sut = CreateProvider();


        ProviderCapabilities capabilities = sut.Capabilities;


        capabilities.CanPeek.ShouldBeTrue();
        capabilities.CanReceiveSettle.ShouldBeTrue();
        capabilities.SupportsLockExpiry.ShouldBeFalse();
        capabilities.SupportsDeadLetterReason.ShouldBeFalse();
        capabilities.SupportsDeadLetterSubQueue.ShouldBeFalse();
        capabilities.SupportsSequenceNumbers.ShouldBeFalse();
        capabilities.SupportsDeferral.ShouldBeFalse();
        capabilities.SupportsScheduling.ShouldBeFalse();
    }

    [Fact]
    public async Task GetRuntimeInfoAsync_Exchange_ReturnsEmptyCountsWithoutBrokerCalls()
    {
        await using RabbitMqProvider sut = CreateProvider();


        EntityRuntimeInfo info = await sut.GetRuntimeInfoAsync("exchanges/events", CancellationToken.None);


        info.ActiveMessageCount.ShouldBe(0);
        await _management.DidNotReceiveWithAnyArgs().GetQueueAsync(default!, default);
    }

    [Fact]
    public async Task PeekAsync_Exchange_ThrowsNotSupported()
    {
        await using RabbitMqProvider sut = CreateProvider();


        await Should.ThrowAsync<NotSupportedByProviderException>(
            () => sut.PeekAsync(new PeekRequest("exchanges/events", MessageSubQueue.Active, 10, null), CancellationToken.None));
    }

    [Fact]
    public async Task PeekAsync_DeadLetterSubQueue_ThrowsNotSupported()
    {
        await using RabbitMqProvider sut = CreateProvider();


        await Should.ThrowAsync<NotSupportedByProviderException>(
            () => sut.PeekAsync(new PeekRequest("queues/orders", MessageSubQueue.DeadLetter, 10, null), CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_ScheduledMessage_ThrowsNotSupported()
    {
        await using RabbitMqProvider sut = CreateProvider();
        ToolboxMessage message = new() { Body = "x"u8.ToArray(), ScheduledEnqueueTime = DateTimeOffset.UnixEpoch };


        await Should.ThrowAsync<NotSupportedByProviderException>(() => sut.SendAsync("queues/orders", [message], CancellationToken.None));
    }

    [Fact]
    public async Task DisposeAsync_Always_DisposesManagementClient()
    {
        RabbitMqProvider sut = CreateProvider();


        await sut.DisposeAsync();


        _management.Received(1).Dispose();
    }

    [Fact]
    public async Task SettleAsync_DeadLetterWithoutDeadLetterExchange_ThrowsBeforeTouchingDeliveries()
    {
        _management.GetQueueAsync("orders", Arg.Any<CancellationToken>()).Returns(new RabbitMqQueueInfo { Name = "orders", Type = "classic" });
        await using RabbitMqProvider sut = CreateProvider();
        await sut.ProbeCapabilitiesAsync(CancellationToken.None);


        NotSupportedByProviderException exception = await Should.ThrowAsync<NotSupportedByProviderException>(() => sut.SettleAsync(
            new SettleRequest("queues/orders", MessageSubQueue.Active, SettlementOperation.DeadLetter, ["token"], null, null),
            CancellationToken.None));


        exception.Message.ShouldContain("dead-letter exchange");
    }

    [Fact]
    public async Task PrepareResubmitAsync_DeadLetteredMessage_ClonesWithoutBrokerPropertiesAndMarksReplay()
    {
        await using RabbitMqProvider sut = CreateProvider();
        ToolboxMessage original = new()
        {
            Body = "{}"u8.ToArray(),
            MessageId = "m-1",
            RoutingKey = "orders",
            ApplicationProperties = new Dictionary<string, object?> { ["tenant"] = "contoso" },
            BrokerProperties = new Dictionary<string, object?> { ["x-death"] = "[…]" },
            LockToken = "token",
        };


        ToolboxMessage clone = await sut.PrepareResubmitAsync("queues/orders.dlq", MessageSubQueue.Active, original, ResubmitOverrides.Empty, CancellationToken.None);


        clone.MessageId.ShouldBe("m-1");
        clone.RoutingKey.ShouldBe("orders");
        clone.LockToken.ShouldBeNull();
        clone.BrokerProperties.ShouldBeEmpty();
        clone.ApplicationProperties["tenant"].ShouldBe("contoso");
        clone.ApplicationProperties["toolbox-replayed-from"].ShouldBe("m-1");
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
