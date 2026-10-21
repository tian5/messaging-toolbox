namespace MessagingToolbox.Application.Core.Models;

/// <summary>A topic and its subscriptions as modeled by the host environment.</summary>
public sealed record ModeledTopicInfo(string Name, IReadOnlyList<string> Subscriptions);
