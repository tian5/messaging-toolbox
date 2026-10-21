using System.Text;
using System.Text.Json;
using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Abstractions.Messaging;
using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;
using MessagingToolbox.Application.Core.Interfaces;

namespace MessagingToolbox.Application.Features.Messages.Queries.ExportMessages;

public sealed class ExportMessagesQueryHandler : IRequestHandler<ExportMessagesQuery, ExportedMessagesFileDto>
{
    private static readonly JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };

    private readonly IMessagingProviderFactory _providerFactory;
    private readonly TimeProvider _timeProvider;

    public ExportMessagesQueryHandler(IMessagingProviderFactory providerFactory, TimeProvider timeProvider)
    {
        _providerFactory = providerFactory;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ExportedMessagesFileDto> Handle(ExportMessagesQuery request, CancellationToken cancellationToken)
    {
        List<MessageDto> export;
        if (request.LoadedMessages is not null)
        {
            export = request.LoadedMessages
                .Where(message => request.SequenceNumbers is null
                    || (message.SequenceNumber is not null && request.SequenceNumbers.Contains(message.SequenceNumber.Value)))
                .Select(dto => dto with { LockToken = null })
                .ToList();
        }
        else
        {
            IMessagingProvider provider = await _providerFactory.GetProviderAsync(request.ConnectionId, cancellationToken);

            PeekRequest peekRequest = new(request.EntityPath, request.SubQueue, request.MaxMessages, request.FromSequenceNumber, request.SessionId);
            IReadOnlyList<ToolboxMessage> messages = await provider.PeekAsync(peekRequest, cancellationToken);

            export = messages
                .Where(message => request.SequenceNumbers is null
                    || (message.SequenceNumber is not null && request.SequenceNumbers.Contains(message.SequenceNumber.Value)))
                .Select(MessageDtoMapper.ToDto)
                .Select(dto => dto with { LockToken = null })
                .ToList();
        }

        string json = JsonSerializer.Serialize(export, _serializerOptions);
        string fileName = $"{Sanitize(request.EntityPath)}-{request.SubQueue}-{_timeProvider.GetUtcNow():yyyyMMdd-HHmmss}.json";

        return new ExportedMessagesFileDto(fileName, Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
    }

    private static string Sanitize(string path)
        => string.Concat(path.Select(character => char.IsLetterOrDigit(character) ? character : '-'));
}
