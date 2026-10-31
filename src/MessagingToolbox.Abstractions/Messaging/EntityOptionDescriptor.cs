namespace MessagingToolbox.Abstractions.Messaging;

/// <summary>
/// One setting a provider accepts when creating or updating an entity, described so the UI can
/// render it without knowing the broker (ADR 21). Its value travels in
/// <see cref="EntityDefinition.Options"/> under <see cref="Key"/>, typed by <see cref="Type"/>.
/// </summary>
/// <param name="Key">The options key the provider reads.</param>
/// <param name="Label">Human-readable label.</param>
/// <param name="Type">Editor and value type.</param>
public sealed record EntityOptionDescriptor(string Key, string Label, EntityOptionType Type)
{
    /// <summary>Help text shown next to the input.</summary>
    public string? Description { get; init; }

    /// <summary>Input placeholder, e.g. <c>hh:mm:ss</c>.</summary>
    public string? Placeholder { get; init; }

    /// <summary>Value of a new entity's option; null leaves it to the broker.</summary>
    public object? DefaultValue { get; init; }

    /// <summary>Allowed values of a <see cref="EntityOptionType.Choice"/>.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>The broker fixes the option when the entity is created; it is shown read-only when editing.</summary>
    public bool CreateOnly { get; init; }
}
