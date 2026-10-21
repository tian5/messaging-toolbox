using Aspire.Hosting.ApplicationModel;

namespace MessagingToolbox.Aspire.Hosting;

/// <summary>
/// Tracks the next free <c>ToolboxOptions__Connections__{i}</c> index on a toolbox host resource,
/// so each <c>WithMessagingToolboxConnection</c> call composes independently, and the brokers
/// already attached, so attaching one twice is a no-op instead of a duplicate connection.
/// </summary>
internal sealed class MessagingToolboxConnectionsAnnotation : IResourceAnnotation
{
    public int NextIndex;

    public HashSet<IResource> AttachedBrokers { get; } = new(ReferenceEqualityComparer.Instance);
}
