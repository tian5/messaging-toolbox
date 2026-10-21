using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Messages.Queries.GetPurgePreview;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Queries.GetPurgePreview;

public class GetPurgePreviewQueryHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly GetPurgePreviewQueryHandler _sut;

    public GetPurgePreviewQueryHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _sut = new GetPurgePreviewQueryHandler(_providerFactory);
    }

    [Fact]
    public async Task Handle_RuntimeInfo_ReturnsCountForSubQueue()
    {
        EntityRuntimeInfo info = new() { ActiveMessageCount = 10, DeadLetterMessageCount = 3, TransferDeadLetterMessageCount = 1 };
        _provider.GetRuntimeInfoAsync("orders", Arg.Any<CancellationToken>()).Returns(info);


        (await _sut.Handle(new GetPurgePreviewQuery(ConnectionId, "orders", MessageSubQueue.Active), CancellationToken.None)).ShouldBe(10);
        (await _sut.Handle(new GetPurgePreviewQuery(ConnectionId, "orders", MessageSubQueue.DeadLetter), CancellationToken.None)).ShouldBe(3);
        (await _sut.Handle(new GetPurgePreviewQuery(ConnectionId, "orders", MessageSubQueue.TransferDeadLetter), CancellationToken.None)).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_RuntimeInfoUnavailable_ReturnsNull()
    {
        _provider
            .GetRuntimeInfoAsync("orders", Arg.Any<CancellationToken>())
            .Throws(new NotSupportedByProviderException("no management plane"));


        long? result = await _sut.Handle(new GetPurgePreviewQuery(ConnectionId, "orders", MessageSubQueue.Active), CancellationToken.None);


        result.ShouldBeNull();
    }
}
