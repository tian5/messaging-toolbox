using System.Text;
using System.Text.Json;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Messages.Commands.ImportMessages;
using MessagingToolbox.Application.Services;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Commands.ImportMessages;

public class ImportMessagesCommandHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly IAuditLog _auditLog;
    private readonly ImportMessagesCommandHandler _sut;

    public ImportMessagesCommandHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _auditLog = Substitute.For<IAuditLog>();
        _sut = new ImportMessagesCommandHandler(_providerFactory, Substitute.For<IConnectionCatalog>(), _auditLog);
    }

    [Fact]
    public async Task Handle_ValidFile_ImportsAndSendsMessages()
    {
        List<MessageDto> exported =
        [
            new() { Body = "one", BodyFormat = MessageBodyFormat.Text, BodySizeBytes = 3, Subject = "first", ApplicationProperties = new Dictionary<string, object?> { ["k"] = "v" } },
            new() { Body = "two", BodyFormat = MessageBodyFormat.Text, BodySizeBytes = 3 },
        ];
        string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(exported)));


        int imported = await _sut.Handle(new ImportMessagesCommand(ConnectionId, "retry-queue", base64), CancellationToken.None);


        imported.ShouldBe(2);
        await _provider.Received(1).SendAsync(
            "retry-queue",
            Arg.Is<IReadOnlyList<ToolboxMessage>>(messages =>
                messages.Count == 2 &&
                Encoding.UTF8.GetString(messages[0].Body) == "one" &&
                messages[0].Subject == "first" &&
                (string?)messages[0].ApplicationProperties["k"] == "v"),
            Arg.Any<CancellationToken>());
        await _auditLog.Received(1).WriteAsync(AuditLog.Import, Arg.Any<string>(), "retry-queue", MessageSubQueue.Active, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidFile_ThrowsMessagingProviderException()
    {
        string base64 = Convert.ToBase64String("not json at all"u8.ToArray());


        MessagingProviderException exception = await Should.ThrowAsync<MessagingProviderException>(
            () => _sut.Handle(new ImportMessagesCommand(ConnectionId, "orders", base64), CancellationToken.None).AsTask());


        exception.Message.ShouldContain("not a valid MessagingToolbox export");
        await _provider.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ToolboxMessage>>(), Arg.Any<CancellationToken>());
    }
}
