using FluentValidation;
using MessagingToolbox.Application.Configuration;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Application.Features.Connections.Commands.SaveConnection;
using MessagingToolbox.Application.Features.Management.Commands.CreateOrUpdateEntity;
using MessagingToolbox.Application.Features.Management.Commands.DeleteEntity;
using MessagingToolbox.Application.Features.Management.Commands.CreateRoutingRule;
using MessagingToolbox.Application.Features.Management.Commands.DeleteRoutingRule;
using MessagingToolbox.Application.Features.Messages.Commands.DeadLetterMessages;
using MessagingToolbox.Application.Features.Messages.Commands.DeleteMessages;
using MessagingToolbox.Application.Features.Messages.Commands.RepairAndResubmitMessage;
using MessagingToolbox.Application.Features.Messages.Commands.RequeueMessages;
using MessagingToolbox.Application.Features.Messages.Commands.SendMessage;
using MessagingToolbox.Application.Features.Messages.Commands.SettleBySequence;
using MessagingToolbox.Application.Features.Messages.Commands.SettleMessages;
using MessagingToolbox.Application.Features.Messages.Queries.ReceiveDeferredMessages;
using MessagingToolbox.Application.Features.Templates.Commands.SaveCannedMessage;
using MessagingToolbox.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MessagingToolbox.Application;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddApplication(IConfiguration configuration)
        {
            services.AddValidators();

            services.AddOptions<ToolboxOptions>()
                .Bind(configuration.GetSection(nameof(ToolboxOptions)));

            services.AddSingleton<IMessageTemplateEngine, MessageTemplateEngine>();
            services.AddScoped<IAuditLog, AuditLog>();

            return services;
        }
    }

    private static void AddValidators(this IServiceCollection services)
    {
        services.AddScoped<IValidator<SaveConnectionCommand>, SaveConnectionCommandValidator>();
        services.AddScoped<IValidator<SaveCannedMessageCommand>, SaveCannedMessageCommandValidator>();
        services.AddScoped<IValidator<SendMessageCommand>, SendMessageCommandValidator>();
        services.AddScoped<IValidator<RepairAndResubmitMessageCommand>, RepairAndResubmitMessageCommandValidator>();
        services.AddScoped<IValidator<DeadLetterMessagesCommand>, DeadLetterMessagesCommandValidator>();
        services.AddScoped<IValidator<DeleteMessagesCommand>, DeleteMessagesCommandValidator>();
        services.AddScoped<IValidator<RequeueMessagesCommand>, RequeueMessagesCommandValidator>();
        services.AddScoped<IValidator<SettleMessagesCommand>, SettleMessagesCommandValidator>();
        services.AddScoped<IValidator<SettleBySequenceCommand>, SettleBySequenceCommandValidator>();
        services.AddScoped<IValidator<ReceiveDeferredMessagesQuery>, ReceiveDeferredMessagesQueryValidator>();
        services.AddScoped<IValidator<CreateOrUpdateEntityCommand>, CreateOrUpdateEntityCommandValidator>();
        services.AddScoped<IValidator<DeleteEntityCommand>, DeleteEntityCommandValidator>();
        services.AddScoped<IValidator<CreateRoutingRuleCommand>, CreateRoutingRuleCommandValidator>();
        services.AddScoped<IValidator<DeleteRoutingRuleCommand>, DeleteRoutingRuleCommandValidator>();
    }
}
