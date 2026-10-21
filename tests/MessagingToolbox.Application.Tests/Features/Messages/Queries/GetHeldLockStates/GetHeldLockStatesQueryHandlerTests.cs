using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Messages.Queries.GetHeldLockStates;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Messages.Queries.GetHeldLockStates;

public class GetHeldLockStatesQueryHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly GetHeldLockStatesQueryHandler _sut;

    public GetHeldLockStatesQueryHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _sut = new GetHeldLockStatesQueryHandler(_providerFactory);
    }

    [Fact]
    public async Task Handle_LocksHeld_ReturnsCurrentExpiriesByToken()
    {
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddMinutes(2);
        _provider.GetHeldLockStatesAsync(Arg.Any<string>(), Arg.Any<MessageSubQueue>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, DateTimeOffset> { ["token-1"] = expiry });


        IReadOnlyDictionary<string, DateTimeOffset> states = await _sut.Handle(
            new GetHeldLockStatesQuery(ConnectionId, "orders", MessageSubQueue.Active, ["token-1", "gone-token"]),
            CancellationToken.None);


        states.ShouldHaveSingleItem();
        states["token-1"].ShouldBe(expiry);
        await _provider.Received(1).GetHeldLockStatesAsync(
            "orders",
            MessageSubQueue.Active,
            Arg.Is<IReadOnlyList<string>>(tokens => tokens.SequenceEqual(new[] { "token-1", "gone-token" })),
            Arg.Any<CancellationToken>());
    }
}
