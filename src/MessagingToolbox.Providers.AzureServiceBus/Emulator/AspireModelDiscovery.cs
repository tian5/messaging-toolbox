using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Core.Models;

namespace MessagingToolbox.Providers.AzureServiceBus.Emulator;

/// <summary>
/// Preferred discovery channel: entities modeled in the Aspire AppHost (AddServiceBusQueue,
/// AddServiceBusTopic, AddServiceBusSubscription) are injected into the toolbox as configuration,
/// so no emulator internals are needed at all (architecture doc §6).
/// </summary>
public sealed class AspireModelDiscovery : IEmulatorEntityDiscovery
{
    private readonly IReadOnlyList<string> _queues;
    private readonly IReadOnlyList<ModeledTopicInfo> _topics;

    public AspireModelDiscovery(IReadOnlyList<string> queues, IReadOnlyList<ModeledTopicInfo> topics)
    {
        _queues = queues;
        _topics = topics;
    }

    public Task<IReadOnlyList<MessagingEntity>?> DiscoverAsync(CancellationToken cancellationToken)
    {
        List<MessagingEntity> entities = [];

        foreach (string queue in _queues)
        {
            entities.Add(new MessagingEntity
            {
                Path = queue,
                Name = queue,
                Kind = EntityKind.Queue,
                Status = MessagingEntityStatus.Active,
            });
        }

        foreach (ModeledTopicInfo topic in _topics)
        {
            entities.Add(new MessagingEntity
            {
                Path = topic.Name,
                Name = topic.Name,
                Kind = EntityKind.Topic,
                Status = MessagingEntityStatus.Active,
            });

            foreach (string subscription in topic.Subscriptions)
            {
                entities.Add(new MessagingEntity
                {
                    Path = $"{topic.Name}/subscriptions/{subscription}",
                    Name = subscription,
                    Kind = EntityKind.Subscription,
                    ParentPath = topic.Name,
                    Status = MessagingEntityStatus.Active,
                });
            }
        }

        return Task.FromResult<IReadOnlyList<MessagingEntity>?>(entities);
    }
}
