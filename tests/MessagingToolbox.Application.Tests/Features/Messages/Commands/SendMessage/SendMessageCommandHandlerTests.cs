using System.Text;
using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Messages.Commands.SendMessage;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Commands.SendMessage;

public class SendMessageCommandHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly IMessageTemplateEngine _templateEngine;
    private readonly SendMessageCommandHandler _sut;

    public SendMessageCommandHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _templateEngine = Substitute.For<IMessageTemplateEngine>();
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _templateEngine.Render(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _sut = new SendMessageCommandHandler(_providerFactory, _templateEngine);
    }

    [Fact]
    public async Task Handle_ValidCommand_SendsMappedMessage()
    {
        Dictionary<string, string> properties = new() { ["tenant"] = "tests" };
        SendMessageCommand command = new(
            ConnectionId, "orders", "{\"orderId\": 7}", "application/json", properties,
            Subject: "order-created", MessageId: "msg-1", CorrelationId: "corr-1", SessionId: "session-1",
            To: null, ReplyTo: null, ScheduledEnqueueTime: null, TimeToLive: null, Copies: 1);


        Unit result = await _sut.Handle(command, CancellationToken.None);


        result.ShouldBe(Unit.Value);
        await _provider.Received(1).SendAsync(
            "orders",
            Arg.Is<IReadOnlyList<ToolboxMessage>>(messages =>
                messages.Count == 1 &&
                Encoding.UTF8.GetString(messages[0].Body) == "{\"orderId\": 7}" &&
                messages[0].ContentType == "application/json" &&
                messages[0].Subject == "order-created" &&
                messages[0].MessageId == "msg-1" &&
                messages[0].CorrelationId == "corr-1" &&
                messages[0].SessionId == "session-1" &&
                (string?)messages[0].ApplicationProperties["tenant"] == "tests"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MultipleCopies_RendersTemplatePerCopy()
    {
        int rendered = 0;
        _templateEngine.Render(Arg.Any<string>()).Returns(_ => $"body-{++rendered}");

        SendMessageCommand command = new(
            ConnectionId, "orders", "template", null, new Dictionary<string, string>(),
            null, null, null, null, null, null, null, null, Copies: 3);


        await _sut.Handle(command, CancellationToken.None);


        await _provider.Received(1).SendAsync(
            "orders",
            Arg.Is<IReadOnlyList<ToolboxMessage>>(messages =>
                messages.Count == 3 &&
                Encoding.UTF8.GetString(messages[0].Body) == "body-1" &&
                Encoding.UTF8.GetString(messages[1].Body) == "body-2" &&
                Encoding.UTF8.GetString(messages[2].Body) == "body-3"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TypedProperties_SendsTypedValues()
    {
        SendMessageCommand command = new(
            ConnectionId, "orders", "body", null,
            new Dictionary<string, string> { ["count"] = "7", ["urgent"] = "true", ["tenant"] = "tests" },
            null, null, null, null, null, null, null, null, Copies: 1,
            PropertyTypes: new Dictionary<string, PropertyValueType> { ["count"] = PropertyValueType.Int32, ["urgent"] = PropertyValueType.Boolean });


        IReadOnlyList<ToolboxMessage>? sent = null;
        await _provider.SendAsync("orders", Arg.Do<IReadOnlyList<ToolboxMessage>>(messages => sent = messages), Arg.Any<CancellationToken>());


        await _sut.Handle(command, CancellationToken.None);


        IReadOnlyDictionary<string, object?> properties = sent.ShouldNotBeNull().ShouldHaveSingleItem().ApplicationProperties;
        properties["count"].ShouldBeOfType<int>().ShouldBe(7);
        properties["urgent"].ShouldBeOfType<bool>().ShouldBeTrue();
        properties["tenant"].ShouldBe("tests");
    }
}
