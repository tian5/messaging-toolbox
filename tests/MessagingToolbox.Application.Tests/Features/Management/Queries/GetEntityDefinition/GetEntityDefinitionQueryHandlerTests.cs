using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Management.Queries.GetEntityDefinition;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Management.Queries.GetEntityDefinition;

public class GetEntityDefinitionQueryHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly GetEntityDefinitionQueryHandler _sut;

    public GetEntityDefinitionQueryHandlerTests()
    {
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _sut = new GetEntityDefinitionQueryHandler(_providerFactory);
    }

    [Fact]
    public async Task Handle_ReturnsMappedDefinition()
    {
        EntityDefinition definition = new(
            "processor",
            EntityKind.Subscription,
            "events",
            new Dictionary<string, object?> { ["LockDuration"] = TimeSpan.FromMinutes(2) });

        _provider.GetEntityDefinitionAsync("events/subscriptions/processor", Arg.Any<CancellationToken>()).Returns(definition);


        EntityDefinitionDto result = await _sut.Handle(
            new GetEntityDefinitionQuery(ConnectionId, "events/subscriptions/processor"),
            CancellationToken.None);


        result.Name.ShouldBe("processor");
        result.Kind.ShouldBe(EntityKind.Subscription);
        result.ParentPath.ShouldBe("events");
        result.Options["LockDuration"].ShouldBe(TimeSpan.FromMinutes(2));
    }
}
