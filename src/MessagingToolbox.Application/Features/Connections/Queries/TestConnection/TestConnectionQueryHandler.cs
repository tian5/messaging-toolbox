using System.Diagnostics;
using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;

namespace MessagingToolbox.Application.Features.Connections.Queries.TestConnection;

public sealed class TestConnectionQueryHandler : IRequestHandler<TestConnectionQuery, TestConnectionResultDto>
{
    private readonly IMessagingProviderFactory _providerFactory;

    public TestConnectionQueryHandler(IMessagingProviderFactory providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public async ValueTask<TestConnectionResultDto> Handle(TestConnectionQuery request, CancellationToken cancellationToken)
    {
        try
        {
            IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

            // Listing the first entity is the lightest broker round-trip available on every provider.
            Stopwatch stopwatch = Stopwatch.StartNew();
            await foreach (MessagingEntity _ in provider.ListEntitiesAsync(cancellationToken))
            {
                break;
            }

            stopwatch.Stop();

            return new TestConnectionResultDto(true, null, stopwatch.Elapsed);
        }
        catch (Exception exception)
        {
            return new TestConnectionResultDto(false, exception.Message, null);
        }
    }
}
