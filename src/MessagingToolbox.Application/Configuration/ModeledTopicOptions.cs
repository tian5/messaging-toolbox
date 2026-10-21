namespace MessagingToolbox.Application.Configuration;

/// <summary>A topic (and its subscriptions) modeled by the host environment.</summary>
public sealed class ModeledTopicOptions
{
    public string Name { get; set; } = string.Empty;

    public List<string> Subscriptions { get; set; } = [];
}
