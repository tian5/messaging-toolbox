using System.Text;
using System.Text.Json;
using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Services;

namespace MessagingToolbox.Application.Features.Messages.Commands.ImportMessages;

public sealed class ImportMessagesCommandHandler : IRequestHandler<ImportMessagesCommand, int>
{
    private const int SendBatchSize = 100;

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;

    public ImportMessagesCommandHandler(
        IMessagingProviderFactory providerFactory,
        IConnectionCatalog connectionCatalog,
        IAuditLog auditLog)
    {
        _providerFactory = providerFactory;
        _connectionCatalog = connectionCatalog;
        _auditLog = auditLog;
    }

    public async ValueTask<int> Handle(ImportMessagesCommand request, CancellationToken cancellationToken)
    {
        List<MessageDto>? messages;
        try
        {
            byte[] json = Convert.FromBase64String(request.Base64Content);
            messages = JsonSerializer.Deserialize<List<MessageDto>>(Encoding.UTF8.GetString(json));
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new MessagingProviderException("The selected file is not a valid MessagingToolbox export.", exception);
        }

        if (messages is null || messages.Count == 0)
        {
            return 0;
        }

        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        int sent = 0;
        foreach (MessageDto[] batch in messages.Chunk(SendBatchSize))
        {
            List<ToolboxMessage> replay = batch
                .Select(dto => MessageDtoMapper.ToToolboxMessage(
                    dto.Body,
                    dto.ContentType,
                    dto.ApplicationProperties.ToDictionary(pair => pair.Key, pair => UnwrapJsonValue(pair.Value)),
                    subject: dto.Subject,
                    messageId: dto.MessageId,
                    correlationId: dto.CorrelationId,
                    sessionId: dto.SessionId,
                    to: dto.To,
                    replyTo: dto.ReplyTo))
                .ToList();

            await provider.SendAsync(request.TargetPath, replay, cancellationToken);
            sent += replay.Count;
        }

        ConnectionInfo? connection = await _connectionCatalog.GetByIdAsync(request.ConnectionId, cancellationToken);
        await _auditLog.WriteAsync(
            AuditLog.Import,
            connection?.Name ?? request.ConnectionId.ToString(),
            request.TargetPath,
            MessageSubQueue.Active,
            $"{sent} message(s) imported from a file",
            cancellationToken);

        return sent;
    }

    // JSON round-trips property values as JsonElement; brokers want primitives back.
    private static object? UnwrapJsonValue(object? value) => value is JsonElement element
        ? element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out long integer) ? integer : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString(),
        }
        : value;
}
