using System.Text;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Messages.Queries.PeekMessages;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Queries.PeekMessages;

public class PeekMessagesQueryHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly PeekMessagesQueryHandler _sut;

    public PeekMessagesQueryHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _sut = new PeekMessagesQueryHandler(_providerFactory);
    }

    [Fact]
    public async Task Handle_Query_PassesSubQueueAndPagingToProvider()
    {
        _provider.PeekAsync(Arg.Any<PeekRequest>(), Arg.Any<CancellationToken>())
            .Returns([]);


        await _sut.Handle(new PeekMessagesQuery(ConnectionId, "orders", MessageSubQueue.DeadLetter, 42, 10), CancellationToken.None);


        await _provider.Received(1).PeekAsync(
            Arg.Is<PeekRequest>(request =>
                request.EntityPath == "orders" &&
                request.SubQueue == MessageSubQueue.DeadLetter &&
                request.FromSequenceNumber == 42 &&
                request.MaxMessages == 10),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Messages_ReturnsMappedMessagesWithMetadata()
    {
        ToolboxMessage jsonMessage = new()
        {
            Body = Encoding.UTF8.GetBytes("{\"orderId\": 7}"),
            ContentType = "application/json",
            MessageId = "msg-1",
            SequenceNumber = 12,
            DeliveryCount = 1,
            EnqueuedTime = new DateTimeOffset(2026, 8, 26, 9, 30, 0, TimeSpan.Zero),
            ApplicationProperties = new Dictionary<string, object?> { ["tenant"] = "tests" },
        };
        ToolboxMessage deadLetteredText = new()
        {
            Body = Encoding.UTF8.GetBytes("plain body"),
            MessageId = "msg-2",
            SequenceNumber = 13,
            DeliveryCount = 10,
            DeadLetterReason = "MaxDeliveryCountExceeded",
            DeadLetterErrorDescription = "Message exceeded the maximum delivery count.",
        };

        _provider.PeekAsync(Arg.Any<PeekRequest>(), Arg.Any<CancellationToken>())
            .Returns([jsonMessage, deadLetteredText]);


        IReadOnlyList<MessageDto> result = await _sut.Handle(
            new PeekMessagesQuery(ConnectionId, "orders", MessageSubQueue.Active, null, 25),
            CancellationToken.None);


        result.Count.ShouldBe(2);

        MessageDto json = result[0];
        json.Body.ShouldBe("{\"orderId\": 7}");
        json.BodyFormat.ShouldBe(MessageBodyFormat.Json);
        json.BodySizeBytes.ShouldBe(jsonMessage.Body.Length);
        json.ContentType.ShouldBe("application/json");
        json.SequenceNumber.ShouldBe(12);
        json.EnqueuedTime.ShouldBe(jsonMessage.EnqueuedTime);
        json.ApplicationProperties["tenant"].ShouldBe("tests");

        MessageDto text = result[1];
        text.BodyFormat.ShouldBe(MessageBodyFormat.Text);
        text.DeliveryCount.ShouldBe(10);
        text.DeadLetterReason.ShouldBe("MaxDeliveryCountExceeded");
        text.DeadLetterErrorDescription.ShouldBe("Message exceeded the maximum delivery count.");
    }
}
