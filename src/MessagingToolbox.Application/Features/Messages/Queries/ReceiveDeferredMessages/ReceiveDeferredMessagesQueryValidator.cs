using FluentValidation;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Messages.Queries.ReceiveDeferredMessages;

public sealed class ReceiveDeferredMessagesQueryValidator : AbstractValidator<ReceiveDeferredMessagesQuery>
{
    public const int MaxMessages = 1000;

    public ReceiveDeferredMessagesQueryValidator()
    {
        RuleFor(query => query.EntityPath).NotEmpty();

        RuleFor(query => query.SubQueue)
            .NotEqual(MessageSubQueue.Scheduled)
            .WithMessage("Scheduled messages are never deferred.");

        RuleFor(query => query.Messages)
            .NotEmpty()
            .Must(messages => messages.Count <= MaxMessages)
            .WithMessage($"Receive at most {MaxMessages} deferred messages at a time.");

        RuleFor(query => query.LockRenewalSeconds)
            .InclusiveBetween(0, 300)
            .When(query => query.LockRenewalSeconds is not null);
    }
}
