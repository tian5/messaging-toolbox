using FluentValidation;

namespace MessagingToolbox.Application.Features.Connections.Commands.SaveConnection;

public sealed class SaveConnectionCommandValidator : AbstractValidator<SaveConnectionCommand>
{
    public SaveConnectionCommandValidator()
    {
        RuleFor(command => command.DisplayName).NotEmpty();
        RuleFor(command => command.ConnectionString).NotEmpty().When(command => command.Id is null); //TODO: validate format
        RuleFor(command => command.Kind).IsInEnum().When(command => command.Id is null);
    }
}
