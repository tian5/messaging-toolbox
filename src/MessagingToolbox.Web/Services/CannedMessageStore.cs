using MessagingToolbox.Application.Common;
using MessagingToolbox.Application.Common.Dtos;
using Mediator;
using MessagingToolbox.Application.Features.Templates.Commands.DeleteCannedMessage;
using MessagingToolbox.Application.Features.Templates.Commands.SaveCannedMessage;
using MessagingToolbox.Application.Features.Templates.Notifications;
using MessagingToolbox.Application.Features.Templates.Queries.GetCannedMessages;

namespace MessagingToolbox.Web.Services;

/// <summary>
/// Scoped (per circuit) owner of the canned message list. Components subscribe to
/// Changed and re-render via InvokeAsync(StateHasChanged); mutations go through Mediator.
/// Follows the NotificationService/ConfirmationService pattern.
/// </summary>
public sealed class CannedMessagesStore(ISender sender) : INotificationHandler<CannedMessageChangedNotification>
{
    public IReadOnlyList<CannedMessageDto> Messages { get; private set; } = [];

    public event Func<Task>? Changed;

    public Task InitializeAsync() => RefreshAsync();

    public Task AddAsync(string name, string folder, string body, string? contentType,
        IReadOnlyDictionary<string, string> properties, IReadOnlyDictionary<string, PropertyValueType>? propertyTypes = null)
    {
        return sender.Send(new SaveCannedMessageCommand(null, name, folder, body, contentType, properties, propertyTypes)).AsTask();
    }

    /// <summary>Overwrites an existing canned message — content, name and folder.</summary>
    public Task UpdateAsync(Guid id, string name, string folder, string body, string? contentType,
        IReadOnlyDictionary<string, string> properties, IReadOnlyDictionary<string, PropertyValueType>? propertyTypes)
    {
        return sender.Send(new SaveCannedMessageCommand(id, name, folder, body, contentType, properties, propertyTypes)).AsTask();
    }

    /// <summary>Renames or moves a canned message; its content stays as saved.</summary>
    public Task RenameAsync(CannedMessageDto message, string name, string folder)
        => UpdateAsync(message.Id, name, folder, message.Body, message.ContentType, message.Properties, message.PropertyTypes);

    public Task DeleteAsync(Guid id)
    {
        return sender.Send(new DeleteCannedMessageCommand(id)).AsTask();
    }

    public async ValueTask Handle(CannedMessageChangedNotification notification, CancellationToken cancellationToken)
    {
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        Messages = await sender.Send(new GetCannedMessagesQuery());
        await NotifyAsync();
    }

    private async Task NotifyAsync()
    {
        if (Changed is not null)
        {
            await Changed.Invoke();
        }
    }
}
