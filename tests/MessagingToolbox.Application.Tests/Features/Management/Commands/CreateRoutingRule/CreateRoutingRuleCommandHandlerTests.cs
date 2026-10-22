using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Management.Commands.CreateRoutingRule;
using MessagingToolbox.Application.Services;
using MessagingToolbox.Application.Tests.Common;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Management.Commands.CreateRoutingRule;

public class CreateRoutingRuleCommandHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private static readonly Dictionary<string, object?> Options = new() { ["routing-key"] = "invoice.*" };

    private readonly IMessagingProvider _provider;
    private readonly IAuditLog _auditLog;
    private readonly CreateRoutingRuleCommandHandler _sut;

    public CreateRoutingRuleCommandHandlerTests()
    {
        IMessagingProviderFactory providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _auditLog = Substitute.For<IAuditLog>();
        _provider.Capabilities.Returns(TestCapabilities.Full);
        providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _sut = new CreateRoutingRuleCommandHandler(
            providerFactory,
            Substitute.For<IConnectionCatalog>(),
            _auditLog);
    }

    [Fact]
    public async Task Handle_RulesManageable_CreatesAndAudits()
    {
        RoutingRule created = new("invoice.*", "exchanges/events", "queues/billing", Options);
        _provider.CreateRoutingRuleAsync("exchanges/events", "queues/billing", Options, Arg.Any<CancellationToken>()).Returns(created);


        RoutingRule rule = await _sut.Handle(new CreateRoutingRuleCommand(ConnectionId, "exchanges/events", "queues/billing", Options), CancellationToken.None);


        rule.ShouldBe(created);
        await _auditLog.Received(1).WriteAsync(
            AuditLog.CreateRoutingRule, Arg.Any<string>(), "exchanges/events", MessageSubQueue.Active, "Routes to queues/billing", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RulesNotManageable_ThrowsWithoutCallingTheBroker()
    {
        _provider.Capabilities.Returns(TestCapabilities.Full with { CanManageRules = false });


        await Should.ThrowAsync<NotSupportedByProviderException>(
            () => _sut.Handle(new CreateRoutingRuleCommand(ConnectionId, "exchanges/events", "queues/billing", Options), CancellationToken.None).AsTask());


        await _provider.DidNotReceiveWithAnyArgs().CreateRoutingRuleAsync(default!, default!, default!, default);
    }
}
