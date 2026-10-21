using System.Security.Cryptography;
using System.Text;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Core.Models;

/// <summary>
/// Describes where a <see cref="ConnectionInfo"/> originated.
/// </summary>
public enum ConnectionSource
{
    /// <summary>Seeded by the host environment (Aspire/env); read-only in the UI.</summary>
    Config,

    /// <summary>User-created and persisted; editable and deletable in the UI.</summary>
    Saved,
}

/// <summary>
/// A resolved, ready-to-use connection.
/// </summary>
public sealed record ConnectionInfo
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public required ProviderKind Kind { get; init; }

    public required string ConnectionString { get; init; }

    /// <summary>Where the connection came from.</summary>
    public required ConnectionSource Source { get; init; }

    /// <summary>Management endpoint supplied by the host, when it can't be derived from the connection string.</summary>
    public string? AdminEndpoint { get; init; }

    /// <summary>Queue names modeled by the host environment (preferred emulator discovery channel).</summary>
    public IReadOnlyList<string> ModeledQueues { get; init; } = [];

    /// <summary>Topics and subscriptions modeled by the host environment.</summary>
    public IReadOnlyList<ModeledTopicInfo> ModeledTopics { get; init; } = [];

    /// <summary>Derives a stable identity from the logical name so provider caching survives scope boundaries.</summary>
    public static Guid StableIdForName(string name)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        return new Guid(hash[..16]);
    }
}
