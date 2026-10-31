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
        /// Registers the Azure Service Bus provider builder. The shared provider factory
        /// (Infrastructure) caches the providers it builds per connection (architecture doc §5).
        /// </summary>
        public IServiceCollection AddAzureServiceBusProvider(IConfiguration configuration)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IMessagingProviderBuilder, AzureServiceBusProviderBuilder>());
            services.TryAddSingleton<IAzureServiceBusManagementFactory, AzureServiceBusManagementFactory>();

            return services;
        }
    }
}
