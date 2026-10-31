using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Providers.RabbitMQ.Management;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MessagingToolbox.Providers.RabbitMQ;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the RabbitMQ provider builder. The shared provider factory (Infrastructure)
        /// caches the providers it builds per connection (architecture doc §5).
        /// </summary>
        public IServiceCollection AddRabbitMqProvider(IConfiguration configuration)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IMessagingProviderBuilder, RabbitMqProviderBuilder>());
            services.TryAddSingleton<IRabbitMqManagementClientFactory, RabbitMqManagementClientFactory>();

            return services;
        }
    }
}
