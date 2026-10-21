using Mediator;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Connections.Commands.SaveConnection;
using MessagingToolbox.Application.Features.Connections.Notifications;
using MessagingToolbox.Application.Tests.Common;
using MessagingToolbox.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Features.Connections.Commands.SaveConnection;

public class SaveConnectionCommandHandlerTests : SqliteTestBase
{
    private readonly IConnectionStringProtector _protector;
    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IPublisher _publisher;
    private readonly ILogger<SaveConnectionCommandHandler> _logger;

    public SaveConnectionCommandHandlerTests()
    {
        _protector = Substitute.For<IConnectionStringProtector>();
        _providerFactory = Substitute.For<IMessagingProviderFactory>();
        _publisher = Substitute.For<IPublisher>();
        _logger = Substitute.For<ILogger<SaveConnectionCommandHandler>>();
        _protector.Protect(Arg.Any<string>()).Returns(call => $"protected:{call.Arg<string>()}");
    }

    [Fact]
    public async Task Handle_NewConnection_ProtectsAndPersists()
    {
        SaveConnectionCommandHandler sut = new(DbContextFactory, _protector, _providerFactory, _publisher, _logger);


        await sut.Handle(new SaveConnectionCommand(null, "Local emulator", "Endpoint=sb://localhost;...", ProviderKind.AzureServiceBus), CancellationToken.None);


        ConnectionProfile persisted = DbContext.ConnectionProfiles.Single();
        persisted.DisplayName.ShouldBe("Local emulator");
        persisted.Kind.ShouldBe(ProviderKind.AzureServiceBus);
        persisted.ProtectedConnectionString.ShouldBe("protected:Endpoint=sb://localhost;...");
        persisted.Id.ShouldNotBe(Guid.Empty);
        await _providerFactory.Received(1).DisposeAsync(persisted.Id);
        await _publisher.Received(1).Publish(Arg.Any<SavedConnectionChangedNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingConnection_UpdatesStoredEntity()
    {
        ConnectionProfile existing = new()
        {
            Id = Guid.NewGuid(),
            DisplayName = "Old",
            Kind = ProviderKind.AzureServiceBus,
            ProtectedConnectionString = "protected:old-cs",
        };
        DbContext.ConnectionProfiles.Add(existing);
        await DbContext.SaveChangesAsync(CancellationToken.None);

        SaveConnectionCommandHandler sut = new(DbContextFactory, _protector, _providerFactory, _publisher, _logger);


        await sut.Handle(new SaveConnectionCommand(existing.Id, "New name", "new-cs", ProviderKind.AzureServiceBus), CancellationToken.None);


        ConnectionProfile persisted = DbContext.ConnectionProfiles.AsNoTracking().Single(profile => profile.Id == existing.Id);
        persisted.DisplayName.ShouldBe("New name");
        persisted.ProtectedConnectionString.ShouldBe("protected:new-cs");
        await _publisher.Received(1).Publish(Arg.Any<SavedConnectionChangedNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingConnectionWithEmptyConnectionString_KeepsStoredConnectionString()
    {
        ConnectionProfile existing = new()
        {
            Id = Guid.NewGuid(),
            DisplayName = "Existing",
            Kind = ProviderKind.AzureServiceBus,
            ProtectedConnectionString = "protected:stored-cs",
        };
        DbContext.ConnectionProfiles.Add(existing);
        await DbContext.SaveChangesAsync(CancellationToken.None);

        SaveConnectionCommandHandler sut = new(DbContextFactory, _protector, _providerFactory, _publisher, _logger);


        await sut.Handle(new SaveConnectionCommand(existing.Id, "Renamed", "   ", ProviderKind.AzureServiceBus), CancellationToken.None);


        ConnectionProfile persisted = DbContext.ConnectionProfiles.AsNoTracking().Single(profile => profile.Id == existing.Id);
        persisted.DisplayName.ShouldBe("Renamed");
        persisted.ProtectedConnectionString.ShouldBe("protected:stored-cs");
        _protector.DidNotReceive().Protect(Arg.Any<string>());
        await _publisher.Received(1).Publish(Arg.Any<SavedConnectionChangedNotification>(), Arg.Any<CancellationToken>());
    }
}
