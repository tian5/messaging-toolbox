namespace MessagingToolbox.Application.Common.Dtos;

/// <summary>Outcome of testing a connection against the broker.</summary>
public sealed record TestConnectionResultDto(
    bool Success,
    string? Error,
    TimeSpan? Latency);
