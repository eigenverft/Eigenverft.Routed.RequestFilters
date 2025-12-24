using System;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Eigenverft.Routed.RequestFilters.Middleware.RequestDelayThrottling
{
    /// <summary>
    /// Registers request delay throttling and its options.
    /// </summary>
    public static class IServiceCollectionExtensions
    {
        /// <summary>
        /// Registers request delay throttling with the standard behavior:
        /// binds from configuration section <c>RequestDelayThrottlingOptions</c> if present,
        /// otherwise uses defaults defined on <see cref="RequestDelayThrottlingOptions"/>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The updated service collection.</returns>
        /// <example>
        /// <code>
        /// builder.Services.AddRequestDelayThrottling();
        /// </code>
        /// </example>
        public static IServiceCollection AddRequestDelayThrottling(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddSingleton(typeof(IDeferredLogger<>), typeof(DeferredLogger<>));
            services.AddOptions();

            services.AddOptions<RequestDelayThrottlingOptions>().BindConfiguration(nameof(RequestDelayThrottlingOptions));

            return services;
        }

        /// <summary>
        /// Registers request delay throttling and applies additional code-based configuration on top of configuration binding.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="manualConfigure">Delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddRequestDelayThrottling(this IServiceCollection services, Action<RequestDelayThrottlingOptions> manualConfigure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manualConfigure);

            services.AddRequestDelayThrottling();
            services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Registers request delay throttling options explicitly from a provided configuration and optionally applies extra code-based configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration root that contains a section named <c>RequestDelayThrottlingOptions</c>.</param>
        /// <param name="manualConfigure">Optional delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddRequestDelayThrottling(this IServiceCollection services, IConfiguration configuration, Action<RequestDelayThrottlingOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            services.TryAddSingleton(typeof(IDeferredLogger<>), typeof(DeferredLogger<>));
            services.AddOptions();

            services.AddOptions<RequestDelayThrottlingOptions>().Bind(configuration.GetSection(nameof(RequestDelayThrottlingOptions)));

            if (manualConfigure != null)
            {
                services.Configure(manualConfigure);
            }

            return services;
        }
    }
}
