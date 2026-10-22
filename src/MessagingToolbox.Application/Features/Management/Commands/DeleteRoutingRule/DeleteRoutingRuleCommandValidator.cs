using FluentValidation;

namespace MessagingToolbox.Application.Features.Management.Commands.DeleteRoutingRule;

public sealed class DeleteRoutingRuleCommandValidator : AbstractValidator<DeleteRoutingRuleCommand>
{
    public DeleteRoutingRuleCommandValidator()
    {
        RuleFor(command => command.ConnectionId).NotEmpty();
        RuleFor(command => command.Rule).NotNull();
        RuleFor(command => command.Rule.Id).NotEmpty().When(command => command.Rule is not null);
        RuleFor(command => command.Rule.SourcePath).NotEmpty().When(command => command.Rule is not null);
        RuleFor(command => command.Rule.DestinationPath).NotEmpty().When(command => command.Rule is not null);
    }
}
