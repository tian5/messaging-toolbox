using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Management.Commands.DeleteRoutingRule;
using MessagingToolbox.Application.Services;
using MessagingToolbox.Application.Tests.Common;
using NSubstitute;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Management.Commands.DeleteRoutingRule;

public class DeleteRoutingRuleCommandHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProvider _provider;
    private readonly IAuditLog _auditLog;
    private readonly DeleteRoutingRuleCommandHandler _sut;

    public DeleteRoutingRuleCommandHandlerTests()
    {
        IMessagingProviderFactory providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _auditLog = Substitute.For<IAuditLog>();
        _provider.Capabilities.Returns(TestCapabilities.Full);
        providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _sut = new DeleteRoutingRuleCommandHandler(
            providerFactory,
            Substitute.For<IConnectionCatalog>(),
            _auditLog);
    }

    [Fact]
    public async Task Handle_Rule_DeletesAndAudits()
    {
        RoutingRule rule = new("invoice.*", "exchanges/events", "queues/billing", new Dictionary<string, object?>());


        await _sut.Handle(new DeleteRoutingRuleCommand(ConnectionId, rule), CancellationToken.None);


        await _provider.Received(1).DeleteRoutingRuleAsync(rule, Arg.Any<CancellationToken>());
        await _auditLog.Received(1).WriteAsync(
            AuditLog.DeleteRoutingRule, Arg.Any<string>(), "exchanges/events", MessageSubQueue.Active, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
