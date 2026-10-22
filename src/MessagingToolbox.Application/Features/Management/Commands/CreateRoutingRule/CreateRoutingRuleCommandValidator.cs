using FluentValidation;

namespace MessagingToolbox.Application.Features.Management.Commands.CreateRoutingRule;

public sealed class CreateRoutingRuleCommandValidator : AbstractValidator<CreateRoutingRuleCommand>
{
    public CreateRoutingRuleCommandValidator()
    {
        RuleFor(command => command.ConnectionId).NotEmpty();
        RuleFor(command => command.SourcePath).NotEmpty().MaximumLength(260);
        RuleFor(command => command.DestinationPath).NotEmpty().MaximumLength(260);
        RuleFor(command => command.Options).NotNull();
    }
}
