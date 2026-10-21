using System.Text;
using System.Text.Json;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Messages.Queries.ExportMessages;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Queries.ExportMessages;

public class ExportMessagesQueryHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly ExportMessagesQueryHandler _sut;

    public ExportMessagesQueryHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _sut = new ExportMessagesQueryHandler(_providerFactory, TimeProvider.System);
    }

    [Fact]
    public async Task Handle_PeekedMessages_ReturnsExportedJsonFile()
    {
        ToolboxMessage first = new() { Body = "one"u8.ToArray(), SequenceNumber = 1, Subject = "first", LockToken = "token-1" };
        ToolboxMessage second = new() { Body = "two"u8.ToArray(), SequenceNumber = 2, Subject = "second" };
        _provider.PeekAsync(Arg.Any<PeekRequest>(), Arg.Any<CancellationToken>()).Returns([first, second]);


        ExportedMessagesFileDto result = await _sut.Handle(
            new ExportMessagesQuery(ConnectionId, "orders", MessageSubQueue.DeadLetter, 100, null, null),
            CancellationToken.None);


        result.FileName.ShouldStartWith("orders-DeadLetter-");
        result.FileName.ShouldEndWith(".json");

        string json = Encoding.UTF8.GetString(Convert.FromBase64String(result.Base64Content));
        List<MessageDto>? exported = JsonSerializer.Deserialize<List<MessageDto>>(json);

        exported.ShouldNotBeNull();
        exported.Count.ShouldBe(2);
        exported[0].Subject.ShouldBe("first");
        exported[0].LockToken.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_SequenceNumberFilter_ReturnsMatchingMessages()
    {
        ToolboxMessage first = new() { Body = "one"u8.ToArray(), SequenceNumber = 1 };
        ToolboxMessage second = new() { Body = "two"u8.ToArray(), SequenceNumber = 2 };
        _provider.PeekAsync(Arg.Any<PeekRequest>(), Arg.Any<CancellationToken>()).Returns([first, second]);

        ExportedMessagesFileDto result = await _sut.Handle(
            new ExportMessagesQuery(ConnectionId, "orders", MessageSubQueue.Active, 100, null, [2]),
            CancellationToken.None);


        string json = Encoding.UTF8.GetString(Convert.FromBase64String(result.Base64Content));
        List<MessageDto>? exported = JsonSerializer.Deserialize<List<MessageDto>>(json);


        exported.ShouldNotBeNull();
        exported.ShouldHaveSingleItem().SequenceNumber.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_LoadedMessages_ReturnsExportWithoutPeeking()
    {
        MessageDto first = new()
        {
            Body = "one",
            BodyFormat = MessageBodyFormat.Text,
            BodySizeBytes = 3,
            SequenceNumber = 1,
            Subject = "first",
            LockToken = "token-1",
        };
        MessageDto second = new()
        {
            Body = "two",
            BodyFormat = MessageBodyFormat.Text,
            BodySizeBytes = 3,
            SequenceNumber = 2,
            Subject = "second",
        };


        ExportedMessagesFileDto result = await _sut.Handle(
            new ExportMessagesQuery(ConnectionId, "orders", MessageSubQueue.Active, 100, null, [2], [first, second]),
            CancellationToken.None);


        await _provider.DidNotReceive().PeekAsync(Arg.Any<PeekRequest>(), Arg.Any<CancellationToken>());

        string json = Encoding.UTF8.GetString(Convert.FromBase64String(result.Base64Content));
        List<MessageDto>? exported = JsonSerializer.Deserialize<List<MessageDto>>(json);

        exported.ShouldNotBeNull();
        exported.ShouldHaveSingleItem().SequenceNumber.ShouldBe(2);
        exported[0].LockToken.ShouldBeNull();
    }
}
