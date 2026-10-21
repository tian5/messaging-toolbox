using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;

namespace MessagingToolbox.Application.Features.Explorer.Queries.GetEntityTree;

public sealed class GetEntityTreeQueryHandler : IRequestHandler<GetEntityTreeQuery, IReadOnlyList<EntityTreeNodeDto>>
{
    private readonly IMessagingProviderFactory _providerFactory;

    public GetEntityTreeQueryHandler(IMessagingProviderFactory providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public async ValueTask<IReadOnlyList<EntityTreeNodeDto>> Handle(GetEntityTreeQuery request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        List<MessagingEntity> entities = [];
        await foreach (MessagingEntity entity in provider.ListEntitiesAsync(cancellationToken))
        {
            entities.Add(entity);
        }

        bool supportsScheduling = provider.Capabilities.SupportsScheduling;
        List<EntityTreeNodeDto> nodes = [];

        foreach (MessagingEntity queue in entities.Where(entity => entity.Kind == EntityKind.Queue).OrderBy(entity => entity.Name))
        {
            nodes.Add(ToNode(queue, SubQueueChildren(queue, supportsScheduling)));
        }

        foreach (MessagingEntity topic in entities.Where(entity => entity.Kind == EntityKind.Topic).OrderBy(entity => entity.Name))
        {
            List<EntityTreeNodeDto> subscriptions = entities
                .Where(entity => entity.Kind == EntityKind.Subscription && entity.ParentPath == topic.Path)
                .OrderBy(entity => entity.Name)
                .Select(subscription => ToNode(subscription, SubQueueChildren(subscription, supportsScheduling)))
                .ToList();

            nodes.Add(ToNode(topic, subscriptions));
        }

        return nodes;
    }

    // FR-2.1: active, scheduled, and dead-letter messages show as equivalent child nodes of their
    // message-bearing entity, so every state is browsable even when no counts are available. The
    // scheduled node is capability-driven — brokers without scheduling never render it (ADR 5).
    private static IReadOnlyList<EntityTreeNodeDto> SubQueueChildren(MessagingEntity entity, bool supportsScheduling)
    {
        List<EntityTreeNodeDto> children =
        [
            SubQueueNode(entity, MessageSubQueue.Active, "Active"),
        ];

        if (supportsScheduling)
        {
            children.Add(SubQueueNode(entity, MessageSubQueue.Scheduled, "Scheduled"));
        }

        children.Add(SubQueueNode(entity, MessageSubQueue.DeadLetter, "DLQ"));
        return children;
    }

    private static EntityTreeNodeDto SubQueueNode(MessagingEntity entity, MessageSubQueue subQueue, string name) => new()
    {
        Name = name,
        Path = entity.Path,
        Kind = entity.Kind,
        SubQueue = subQueue,
        IsSubQueueNode = true,
        Status = entity.Status,
        RequiresSession = entity.RequiresSession,
        RuntimeInfo = ToRuntimeInfo(entity.RuntimeInfo),
    };

    private static EntityTreeNodeDto ToNode(MessagingEntity entity, IReadOnlyList<EntityTreeNodeDto> children) => new()
    {
        Name = entity.Name,
        Path = entity.Path,
        Kind = entity.Kind,
        Status = entity.Status,
        RequiresSession = entity.RequiresSession,
        RuntimeInfo = ToRuntimeInfo(entity.RuntimeInfo),
        Children = children,
    };

    private static EntityRuntimeInfoDto? ToRuntimeInfo(EntityRuntimeInfo? runtimeInfo) => runtimeInfo is null
        ? null
        : new EntityRuntimeInfoDto(
            runtimeInfo.ActiveMessageCount,
            runtimeInfo.ScheduledMessageCount,
            runtimeInfo.DeadLetterMessageCount,
            runtimeInfo.TransferDeadLetterMessageCount,
            runtimeInfo.SizeInBytes,
            runtimeInfo.UpdatedAt,
            runtimeInfo.IsApproximate,
            runtimeInfo.DeferredMessageCount);
}
