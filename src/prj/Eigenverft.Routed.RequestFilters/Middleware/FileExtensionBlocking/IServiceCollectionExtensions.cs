using System;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Eigenverft.Routed.RequestFilters.Middleware.FileExtensionBlocking
{
    /// <summary>
    /// Provides extension methods for configuring file extension and path-pattern blocking.
    /// </summary>
    public static partial class IServiceCollectionExtensions
    {
        /// <summary>
        /// Registers file extension blocking with the standard behavior:
        /// binds from configuration section <c>FileExtensionBlockingOptions</c> if present,
        /// otherwise uses defaults defined on <see cref="FileExtensionBlockingOptions"/>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddFileExtensionBlocking(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);

            services.AddOptions<FileExtensionBlockingOptions>().BindConfiguration(nameof(FileExtensionBlockingOptions));

            return services;
        }

        /// <summary>
        /// Registers file extension blocking and applies additional code-based configuration on top of configuration binding.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="manualConfigure">Delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddFileExtensionBlocking(this IServiceCollection services, Action<FileExtensionBlockingOptions> manualConfigure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manualConfigure);

            services.AddFileExtensionBlocking();
            services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Registers file extension blocking options explicitly from a provided configuration and optionally applies extra code-based configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration root that contains a section named <c>FileExtensionBlockingOptions</c>.</param>
        /// <param name="manualConfigure">Optional delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddFileExtensionBlocking(this IServiceCollection services, IConfiguration configuration, Action<FileExtensionBlockingOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);

            services.AddOptions<FileExtensionBlockingOptions>().Bind(configuration.GetSection(nameof(FileExtensionBlockingOptions)));

            if (manualConfigure != null)
            {
                services.Configure(manualConfigure);
            }

            return services;
        }

        private static void AddInfrastructure(IServiceCollection services)
        {
            services.TryAddSingleton(typeof(IDeferredLogger<>), typeof(DeferredLogger<>));
            services.AddOptions();
        }
    }
}
