using MessagingToolbox.Application.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MessagingToolbox.Providers.AzureServiceBus;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the Azure Service Bus provider. The factory is a singleton because AMQP
        /// clients are expensive and cached per connection, while Mediator handlers stay scoped
        /// (architecture doc §5).
        /// </summary>
        public IServiceCollection AddAzureServiceBusProvider(IConfiguration configuration)
        {
            services.TryAddSingleton<IMessagingProviderFactory, MessagingProviderFactory>();
            services.TryAddSingleton<IAzureServiceBusManagementFactory, AzureServiceBusManagementFactory>();

            return services;
        }
    }
}
