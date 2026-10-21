using FluentValidation;
using MessagingToolbox.Abstractions.Messaging;

namespace MessagingToolbox.Application.Features.Management.Commands.CreateOrUpdateEntity;

public sealed class CreateOrUpdateEntityCommandValidator : AbstractValidator<CreateOrUpdateEntityCommand>
{
    public CreateOrUpdateEntityCommandValidator()
    {
        RuleFor(command => command.ConnectionId).NotEmpty();
        RuleFor(command => command.Path).NotEmpty().MaximumLength(260);
        RuleFor(command => command.Kind).IsInEnum();
        RuleFor(command => command.ParentPath)
            .NotEmpty()
            .When(command => command.Kind == EntityKind.Subscription);
    }
}
