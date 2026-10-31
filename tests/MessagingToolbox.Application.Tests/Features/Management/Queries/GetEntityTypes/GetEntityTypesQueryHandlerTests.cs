using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Management.Queries.GetEntityTypes;
using MessagingToolbox.Application.Tests.Common;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Management.Queries.GetEntityTypes;

public class GetEntityTypesQueryHandlerTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private static readonly EntityTypeDescriptor Queue = new(EntityKind.Queue, "queue", [new EntityOptionDescriptor("durable", "Durable", EntityOptionType.Boolean)]);

    private readonly IMessagingProvider _provider;
    private readonly GetEntityTypesQueryHandler _sut;

    public GetEntityTypesQueryHandlerTests()
    {
        IMessagingProviderFactory providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _provider.GetEntityTypes().Returns([Queue]);
        providerFactory.GetProviderAsync(ConnectionId, Arg.Any<CancellationToken>()).Returns(_provider);
        _sut = new GetEntityTypesQueryHandler(providerFactory);
    }

    [Fact]
    public async Task Handle_ManagementAvailable_ReturnsProviderTypes()
    {
        _provider.Capabilities.Returns(TestCapabilities.Full);


        IReadOnlyList<EntityTypeDescriptor> types = await _sut.Handle(new GetEntityTypesQuery(ConnectionId), CancellationToken.None);


        types.ShouldBe([Queue]);
    }

    [Fact]
    public async Task Handle_ManagementUnavailable_ReturnsNone()
    {
        _provider.Capabilities.Returns(TestCapabilities.Full with { CanManageEntities = false });


        IReadOnlyList<EntityTypeDescriptor> types = await _sut.Handle(new GetEntityTypesQuery(ConnectionId), CancellationToken.None);


        types.ShouldBeEmpty();
    }
}
