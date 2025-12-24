using System;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Eigenverft.Routed.RequestFilters.Middleware.RequestLogging
{
    /// <summary>
    /// Provides extension methods for registering <see cref="RequestLoggingMiddleware"/> and its options.
    /// </summary>
    public static partial class IServiceCollectionExtensions
    {
        /// <summary>
        /// Registers request logging with the standard behavior:
        /// binds from configuration section <c>RequestLoggingOptions</c> if present,
        /// otherwise uses defaults defined on <see cref="RequestLoggingOptions"/>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddRequestLogging(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);

            services.AddOptions<RequestLoggingOptions>().BindConfiguration(nameof(RequestLoggingOptions));

            return services;
        }

        /// <summary>
        /// Registers request logging and applies additional code-based configuration on top of configuration binding.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="manualConfigure">Delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddRequestLogging(this IServiceCollection services, Action<RequestLoggingOptions> manualConfigure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manualConfigure);

            services.AddRequestLogging();
            services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Registers request logging options explicitly from a provided configuration and optionally applies extra code-based configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration root that contains a section named <c>RequestLoggingOptions</c>.</param>
        /// <param name="manualConfigure">Optional delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddRequestLogging(this IServiceCollection services, IConfiguration configuration, Action<RequestLoggingOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);

            services.AddOptions<RequestLoggingOptions>().Bind(configuration.GetSection(nameof(RequestLoggingOptions)));

            if (manualConfigure != null)
            {
                services.Configure(manualConfigure);
            }

            return services;
        }

        /// <summary>
        /// Adds shared registrations required by request logging.
        /// </summary>
        /// <param name="services">The service collection.</param>
        private static void AddInfrastructure(IServiceCollection services)
        {
            services.TryAddSingleton(typeof(IDeferredLogger<>), typeof(DeferredLogger<>));
            services.AddOptions();
        }
    }
}
