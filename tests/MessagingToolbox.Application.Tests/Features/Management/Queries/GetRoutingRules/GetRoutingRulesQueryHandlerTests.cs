using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Management.Queries.GetRoutingRules;
using MessagingToolbox.Application.Tests.Common;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Management.Queries.GetRoutingRules;

public class GetRoutingRulesQueryHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private static readonly RoutingRuleType Binding = new("binding", [EntityKind.Exchange], [EntityKind.Queue], []);

    private readonly IMessagingProvider _provider;
    private readonly GetRoutingRulesQueryHandler _sut;

    public GetRoutingRulesQueryHandlerTests()
    {
        IMessagingProviderFactory providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _provider.Capabilities.Returns(TestCapabilities.Full);
        providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _sut = new GetRoutingRulesQueryHandler(providerFactory);
    }

    [Fact]
    public async Task Handle_ProviderWithRules_ReturnsTypeAndRules()
    {
        RoutingRule rule = new("~", "exchanges/events", "queues/billing", new Dictionary<string, object?>());
        _provider.GetRoutingRuleType().Returns(Binding);
        _provider.ListRoutingRulesAsync("queues/billing", Arg.Any<CancellationToken>()).Returns([rule]);


        RoutingRulesDto result = await _sut.Handle(new GetRoutingRulesQuery(ConnectionId, "queues/billing"), CancellationToken.None);


        result.Type.ShouldBe(Binding);
        result.CanManage.ShouldBeTrue();
        result.Rules.ShouldBe([rule]);
    }

    [Fact]
    public async Task Handle_ProviderWithoutRules_ReturnsNoneWithoutListing()
    {
        _provider.GetRoutingRuleType().Returns((RoutingRuleType?)null);


        RoutingRulesDto result = await _sut.Handle(new GetRoutingRulesQuery(ConnectionId, "orders"), CancellationToken.None);


        result.ShouldBe(RoutingRulesDto.None);
        await _provider.DidNotReceiveWithAnyArgs().ListRoutingRulesAsync(default!, default);
    }
}
