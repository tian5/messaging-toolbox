using FluentValidation;

namespace MessagingToolbox.Application.Features.Messages.Queries.PeekMessages;

public sealed class PeekMessagesQueryValidator : AbstractValidator<PeekMessagesQuery>
{
    public PeekMessagesQueryValidator()
    {
        RuleFor(query => query.ConnectionId).NotEmpty();
        RuleFor(query => query.EntityPath).NotEmpty();
        RuleFor(query => query.FromTime)
            .Null()
            .When(query => query.FromSequenceNumber is not null)
            .WithMessage("Start from a sequence number or from a time, not both.");
    }
}
