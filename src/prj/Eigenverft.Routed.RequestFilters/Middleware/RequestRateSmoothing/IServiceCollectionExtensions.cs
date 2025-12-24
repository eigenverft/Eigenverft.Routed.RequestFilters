using System;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Eigenverft.Routed.RequestFilters.Middleware.RequestRateSmoothing
{
    /// <summary>
    /// Registers request rate smoothing and its options.
    /// </summary>
    public static class IServiceCollectionExtensions
    {
        /// <summary>
        /// Registers request rate smoothing with the standard behavior:
        /// binds from configuration section named <see cref="RequestRateSmoothingOptions"/> if present,
        /// otherwise uses defaults defined on <see cref="RequestRateSmoothingOptions"/>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The updated service collection.</returns>
        /// <example>
        /// <code>
        /// builder.Services.AddRequestRateSmoothing();
        /// </code>
        /// </example>
        public static IServiceCollection AddRequestRateSmoothing(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddSingleton(typeof(IDeferredLogger<>), typeof(DeferredLogger<>));
            services.AddOptions();

            services.AddOptions<RequestRateSmoothingOptions>().BindConfiguration(nameof(RequestRateSmoothingOptions));

            return services;
        }

        /// <summary>
        /// Registers request rate smoothing and applies additional code-based configuration on top of configuration binding.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="manualConfigure">Delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddRequestRateSmoothing(this IServiceCollection services, Action<RequestRateSmoothingOptions> manualConfigure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manualConfigure);

            services.AddRequestRateSmoothing();
            services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Registers request rate smoothing options explicitly from a provided configuration and optionally applies extra code-based configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration root that contains a section named <see cref="RequestRateSmoothingOptions"/>.</param>
        /// <param name="manualConfigure">Optional delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddRequestRateSmoothing(this IServiceCollection services, IConfiguration configuration, Action<RequestRateSmoothingOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            services.TryAddSingleton(typeof(IDeferredLogger<>), typeof(DeferredLogger<>));
            services.AddOptions();

            services.AddOptions<RequestRateSmoothingOptions>().Bind(configuration.GetSection(nameof(RequestRateSmoothingOptions)));

            if (manualConfigure != null)
            {
                services.Configure(manualConfigure);
            }

            return services;
        }
    }
}
