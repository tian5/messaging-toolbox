using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Connections.Queries.TestConnection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Connections.Queries.TestConnection;

public class TestConnectionQueryHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly TestConnectionQueryHandler _sut;

    public TestConnectionQueryHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _sut = new TestConnectionQueryHandler(_providerFactory);
    }

    [Fact]
    public async Task Handle_BrokerResponds_ReturnsSuccessWithLatency()
    {
        MessagingEntity queue = new() { Path = "orders", Name = "orders", Kind = EntityKind.Queue };
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _provider.ListEntitiesAsync(Arg.Any<CancellationToken>()).Returns(TestAsyncEnumerable.Of(queue));


        TestConnectionResultDto result = await _sut.Handle(new TestConnectionQuery(ConnectionId), CancellationToken.None);


        result.Success.ShouldBeTrue();
        result.Error.ShouldBeNull();
        result.Latency.ShouldNotBeNull();
    }

    [Fact]
    public async Task Handle_ProviderFails_ReturnsErrorWithMessage()
    {
        _providerFactory
            .GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>())
            .Throws(new MessagingProviderException("cannot connect"));


        TestConnectionResultDto result = await _sut.Handle(new TestConnectionQuery(ConnectionId), CancellationToken.None);


        result.Success.ShouldBeFalse();
        result.Error.ShouldBe("cannot connect");
        result.Latency.ShouldBeNull();
    }
}
