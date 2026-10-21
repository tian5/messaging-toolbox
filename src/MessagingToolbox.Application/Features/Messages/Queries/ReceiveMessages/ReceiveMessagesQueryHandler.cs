using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Errors;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Core.Models;
using MessagingToolbox.Application.Services;

namespace MessagingToolbox.Application.Features.Messages.Queries.ReceiveMessages;

public sealed class ReceiveMessagesQueryHandler : IRequestHandler<ReceiveMessagesQuery, IReadOnlyList<MessageDto>>
{
    private static readonly TimeSpan _defaultMaxWaitTime = TimeSpan.FromSeconds(10);

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly IConnectionCatalog _connectionCatalog;
    private readonly IAuditLog _auditLog;

    public ReceiveMessagesQueryHandler(
        IMessagingProviderFactory providerFactory,
        IConnectionCatalog connectionCatalog,
        IAuditLog auditLog)
    {
        _providerFactory = providerFactory;
        _connectionCatalog = connectionCatalog;
        _auditLog = auditLog;
    }

    public async ValueTask<IReadOnlyList<MessageDto>> Handle(ReceiveMessagesQuery request, CancellationToken cancellationToken)
    {
        IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

        if (request.SessionId is not null && !provider.Capabilities.SupportsSessions)
        {
            throw new NotSupportedByProviderException("This connection does not support sessions.");
        }

        ReceiveRequest receiveRequest = new(
            request.EntityPath,
            request.SubQueue,
            request.MaxMessages,
            request.MaxWaitTime ?? _defaultMaxWaitTime,
            request.SessionId);

        IReadOnlyList<ToolboxMessage> messages = await provider.ReceiveAsync(receiveRequest, cancellationToken);

        if (messages.Count > 0)
        {
            string connectionName = await GetConnectionNameAsync(request.ConnectionId, cancellationToken);
            await _auditLog.WriteAsync(
                AuditLog.ReceiveDelete,
                connectionName,
                request.EntityPath,
                request.SubQueue,
                $"{messages.Count} message(s) deleted (sequences: {DescribeSequences(messages)})",
                cancellationToken);
        }

        return messages.Select(MessageDtoMapper.ToDto).ToList();
    }

    private async Task<string> GetConnectionNameAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        ConnectionInfo? connection = await _connectionCatalog.GetByIdAsync(connectionId, cancellationToken);
        return connection?.Name ?? connectionId.ToString();
    }

    private static string DescribeSequences(IReadOnlyList<ToolboxMessage> messages)
    {
        IEnumerable<string> sequences = messages
            .Where(message => message.SequenceNumber is not null)
            .Select(message => message.SequenceNumber!.Value.ToString())
            .Take(20);

        return string.Join(", ", sequences) + (messages.Count > 20 ? ", …" : string.Empty);
    }
}
