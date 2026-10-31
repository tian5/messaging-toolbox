using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests.Infrastructure.Messaging;

public class MessagingProviderFactoryTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IMessagingProviderBuilder _builder;
    private readonly IMessagingProvider _provider;
    private readonly MessagingProviderFactory _sut;

    public MessagingProviderFactoryTests()
    {
        _connectionCatalog = Substitute.For<IConnectionCatalog>();
        _builder = Substitute.For<IMessagingProviderBuilder>();
        _provider = Substitute.For<IMessagingProvider>();
        _builder.Kind.Returns(ProviderKind.AzureServiceBus);
        _builder.CreateAsync(Arg.Any<ConnectionInfo>(), Arg.Any<CancellationToken>()).Returns(_provider);

        _connectionCatalog.GetByIdAsync(ConnectionId, Arg.Any<CancellationToken>())
            .Returns(new ConnectionInfo
            {
                Id = ConnectionId,
                Name = "messaging",
                DisplayName = "messaging",
                Kind = ProviderKind.AzureServiceBus,
                ConnectionString = "Endpoint=sb://localhost",
                Source = ConnectionSource.Config,
            });

        _sut = new MessagingProviderFactory(_connectionCatalog, [_builder], NullLogger<MessagingProviderFactory>.Instance);
    }

    [Fact]
    public async Task GetProviderAsync_SecondCallForSameConnection_ReturnsSameProviderWithoutRebuilding()
    {
        IMessagingProvider first = await _sut.GetProviderAsync(ConnectionId, CancellationToken.None);
        IMessagingProvider second = await _sut.GetProviderAsync(ConnectionId, CancellationToken.None);


        first.ShouldBeSameAs(second);
        await _builder.Received(1).CreateAsync(Arg.Any<ConnectionInfo>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetProviderAsync_NoBuilderForKind_ThrowsNotSupported()
    {
        MessagingProviderFactory sut = new(_connectionCatalog, [], NullLogger<MessagingProviderFactory>.Instance);


        await Should.ThrowAsync<NotSupportedByProviderException>(() => sut.GetProviderAsync(ConnectionId, CancellationToken.None));
    }

    [Fact]
    public async Task GetProviderAsync_UnknownConnection_ThrowsProviderException()
    {
        await Should.ThrowAsync<MessagingProviderException>(() => _sut.GetProviderAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetProviderAsync_FirstCreationFails_EvictsEntryAndRetriesOnNextCall()
    {
        _builder.CreateAsync(Arg.Any<ConnectionInfo>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("no broker"), _ => Task.FromResult(_provider));


        await Should.ThrowAsync<InvalidOperationException>(() => _sut.GetProviderAsync(ConnectionId, CancellationToken.None));
        IMessagingProvider provider = await _sut.GetProviderAsync(ConnectionId, CancellationToken.None);


        provider.ShouldBeSameAs(_provider);
    }

    [Fact]
    public async Task GetProviderAsync_FirstCallerCancelled_DoesNotPoisonCacheForNextCall()
    {
        TaskCompletionSource<IMessagingProvider> creation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _builder.CreateAsync(Arg.Any<ConnectionInfo>(), Arg.Any<CancellationToken>()).Returns(creation.Task);
        using CancellationTokenSource cancellation = new();

        Task<IMessagingProvider> first = _sut.GetProviderAsync(ConnectionId, cancellation.Token);


        // The caller gives up while creation is still running; the in-flight creation is not
        // tied to that token and must finish for the next caller.
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => first);

        creation.SetResult(_provider);
        IMessagingProvider provider = await _sut.GetProviderAsync(ConnectionId, CancellationToken.None);


        provider.ShouldBeSameAs(_provider);
        await _builder.Received(1).CreateAsync(Arg.Any<ConnectionInfo>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposeAsync_CachedConnection_DisposesProviderAndRebuildsOnNextCall()
    {
        await _sut.GetProviderAsync(ConnectionId, CancellationToken.None);


        await _sut.DisposeAsync(ConnectionId);
        await _sut.GetProviderAsync(ConnectionId, CancellationToken.None);


        await _provider.Received(1).DisposeAsync();
        await _builder.Received(2).CreateAsync(Arg.Any<ConnectionInfo>(), Arg.Any<CancellationToken>());
    }
}
