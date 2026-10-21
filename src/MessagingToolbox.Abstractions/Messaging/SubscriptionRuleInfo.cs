namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>
/// A subscription rule (filter + optional action). Read/written by the management plane
/// </summary>
public sealed record SubscriptionRuleInfo
{
    public required string Name { get; init; }

    /// <summary>Filter kind, e.g. "Sql" or "Correlation".</summary>
    public required string FilterKind { get; init; }

    /// <summary>Filter expression (SQL where-clause) or null for correlation filters.</summary>
    public string? Expression { get; init; }

    /// <summary>Rule action expression, when the rule mutates messages.</summary>
    public string? Action { get; init; }
}
