using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Features.Connections.Queries.GetConnections;
using MessagingToolbox.Application.Tests.Common;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Connections.Queries.GetConnections;

public class GetConnectionsQueryHandlerTests
{
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IMessagingProvider _provider;
    private readonly GetConnectionsQueryHandler _sut;

    public GetConnectionsQueryHandlerTests()
    {
        _connectionCatalog = Substitute.For<IConnectionCatalog>();
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _provider = Substitute.For<IMessagingProvider>();
        _sut = new GetConnectionsQueryHandler(_connectionCatalog, _providerFactory, Substitute.For<ILogger<GetConnectionsQueryHandler>>());
    }

    [Fact]
    public async Task Handle_ConnectionWithProviderCapabilities_ReturnsMappedConnection()
    {
        ConnectionInfo connection = CreateConnection();
        ProviderCapabilities capabilities = TestCapabilities.Full with { MaxMessageSizeBytes = 1024 };

        _connectionCatalog.GetAllAsync(Arg.Any<CancellationToken>()).Returns([connection]);
        _providerFactory.GetProviderAsync(connection.Id, Arg.Any<CancellationToken>()).Returns(_provider);
        _provider.Capabilities.Returns(capabilities);


        IReadOnlyList<ConnectionDto> result = await _sut.Handle(new GetConnectionsQuery(), CancellationToken.None);


        result.Count.ShouldBe(1);
        ConnectionDto dto = result[0];
        dto.Id.ShouldBe(connection.Id);
        dto.Name.ShouldBe("messaging");
        dto.DisplayName.ShouldBe("Messaging (Aspire)");
        dto.Kind.ShouldBe(ProviderKind.AzureServiceBus);
        dto.Source.ShouldBe(ConnectionSource.Config);
        dto.Capabilities.ShouldBe(capabilities);
    }

    [Fact]
    public async Task Handle_ProviderThrowsException_ReturnsConnectionWithoutCapabilities()
    {
        ConnectionInfo connection = CreateConnection();

        _connectionCatalog.GetAllAsync(Arg.Any<CancellationToken>()).Returns([connection]);
        _providerFactory
            .GetProviderAsync(connection.Id, Arg.Any<CancellationToken>())
            .Throws(new MessagingProviderException("boom"));


        IReadOnlyList<ConnectionDto> result = await _sut.Handle(new GetConnectionsQuery(), CancellationToken.None);


        result.Count.ShouldBe(1);
        result[0].Capabilities.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_NoConnections_ReturnsEmptyList()
    {
        _connectionCatalog.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);


        IReadOnlyList<ConnectionDto> result = await _sut.Handle(new GetConnectionsQuery(), CancellationToken.None);


        result.ShouldBeEmpty();
    }

    private static ConnectionInfo CreateConnection() => new()
    {
        Id = ConnectionInfo.StableIdForName("messaging"),
        Name = "messaging",
        DisplayName = "Messaging (Aspire)",
        Kind = ProviderKind.AzureServiceBus,
        ConnectionString = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake;UseDevelopmentEmulator=true",
        Source = ConnectionSource.Config,
    };
}
