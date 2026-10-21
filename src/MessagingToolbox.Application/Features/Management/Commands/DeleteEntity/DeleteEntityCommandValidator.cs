using FluentValidation;

namespace MessagingToolbox.Application.Features.Management.Commands.DeleteEntity;

public sealed class DeleteEntityCommandValidator : AbstractValidator<DeleteEntityCommand>
{
    public DeleteEntityCommandValidator()
    {
        RuleFor(command => command.ConnectionId).NotEmpty();
        RuleFor(command => command.EntityPath).NotEmpty().MaximumLength(260);
    }
}
