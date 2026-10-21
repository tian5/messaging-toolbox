using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Explorer.Queries.GetEntityRuntimeInfo;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Explorer.Queries.GetEntityRuntimeInfo;

public class GetEntityRuntimeInfoQueryHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly GetEntityRuntimeInfoQueryHandler _sut;

    public GetEntityRuntimeInfoQueryHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _sut = new GetEntityRuntimeInfoQueryHandler(_providerFactory);
    }

    [Fact]
    public async Task Handle_RuntimeInfo_ReturnsMappedDto()
    {
        EntityRuntimeInfo info = new()
        {
            ActiveMessageCount = 5,
            ScheduledMessageCount = 1,
            DeadLetterMessageCount = 2,
            TransferDeadLetterMessageCount = 0,
            SizeInBytes = 8192,
            UpdatedAt = new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero),
        };

        _provider.GetRuntimeInfoAsync("orders", Arg.Any<CancellationToken>()).Returns(info);


        EntityRuntimeInfoDto? result = await _sut.Handle(new GetEntityRuntimeInfoQuery(ConnectionId, "orders"), CancellationToken.None);


        result.ShouldNotBeNull();
        result.ActiveMessageCount.ShouldBe(5);
        result.ScheduledMessageCount.ShouldBe(1);
        result.DeadLetterMessageCount.ShouldBe(2);
        result.SizeInBytes.ShouldBe(8192);
        result.UpdatedAt.ShouldBe(info.UpdatedAt);
    }

    [Fact]
    public async Task Handle_NoManagementPlane_ReturnsNull()
    {
        _provider
            .GetRuntimeInfoAsync("orders", Arg.Any<CancellationToken>())
            .Throws(new NotSupportedByProviderException("no management plane"));


        EntityRuntimeInfoDto? result = await _sut.Handle(new GetEntityRuntimeInfoQuery(ConnectionId, "orders"), CancellationToken.None);


        result.ShouldBeNull();
    }
}
