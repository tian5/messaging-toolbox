using MessagingToolbox.Application.Configuration;
using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Domain.Common;
using MessagingToolbox.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MessagingToolbox.Infrastructure;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers toolbox persistence: SQLite in a file under the data directory (a mounted
        /// volume in the container), Data Protection for connection strings, the merging
        /// connection catalog, and the startup schema initializer.
        /// </summary>
        public IServiceCollection AddInfrastructure(IConfiguration configuration)
        {
            services.AddSingleton(TimeProvider.System);

            string dataDirectory = configuration.GetSection(nameof(ToolboxOptions)).GetValue<string>(nameof(ToolboxOptions.DataDirectory))
                ?? AppConstants.ToolboxDataDirectoryName;
            Directory.CreateDirectory(dataDirectory);

            string connectionString = configuration.GetConnectionString(AppConstants.ToolboxDatabaseConnectionName)
                ?? $"Data Source={Path.Combine(dataDirectory, AppConstants.ToolboxDatabaseFileName)}";

            services.AddDbContextFactory<ToolboxDbContext>(options => options.UseSqlite(connectionString));
            services.AddSingleton<IToolboxDbContextFactory, ToolboxDbContextFactory>();

            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
            services.AddSingleton<IConnectionStringProtector, ConnectionStringProtector>();

            services.AddSingleton<IConnectionCatalog, ConnectionCatalog>();
            services.AddHostedService<ToolboxDbInitializer>();

            return services;
        }
    }
}
