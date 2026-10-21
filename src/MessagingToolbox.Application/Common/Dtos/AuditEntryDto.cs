namespace MessagingToolbox.Application.Common.Dtos;

public sealed record AuditEntryDto(
    DateTimeOffset CreatedAt,
    string Action,
    string ConnectionName,
    string EntityPath,
    string SubQueue,
    string? Details);
