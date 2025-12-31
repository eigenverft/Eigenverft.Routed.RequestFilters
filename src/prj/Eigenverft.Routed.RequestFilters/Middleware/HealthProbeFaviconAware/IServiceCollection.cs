using System;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Eigenverft.Routed.RequestFilters.Middleware.HealthProbeFaviconAware
{
    /// <summary>
    /// Provides extension methods for registering <see cref="HealthProbeFaviconAwareOptions"/>.
    /// </summary>
    public static class HealthProbeServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="HealthProbeFaviconAwareOptions"/> using the standard behavior:
        /// binds from configuration section <c>HealthProbeOptions</c> if present,
        /// otherwise uses defaults defined on <see cref="HealthProbeFaviconAwareOptions"/>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The updated service collection.</returns>
        /// <example>
        /// <code>
        /// builder.Services.AddHealthProbe();
        /// </code>
        /// </example>
        public static IServiceCollection AddHealthProbeFaviconAware(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);

            services.AddOptions<HealthProbeFaviconAwareOptions>().BindConfiguration(nameof(HealthProbeFaviconAwareOptions));

            return services;
        }

        /// <summary>
        /// Registers <see cref="HealthProbeFaviconAwareOptions"/> and applies additional code-based configuration.
        /// The extra configuration is applied on top of configuration binding.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="manualConfigure">Delegate to modify or augment <see cref="HealthProbeFaviconAwareOptions"/>.</param>
        /// <returns>The updated service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="manualConfigure"/> is null.</exception>
        public static IServiceCollection AddHealthProbeFaviconAware(this IServiceCollection services, Action<HealthProbeFaviconAwareOptions> manualConfigure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manualConfigure);

            services.AddHealthProbeFaviconAware();
            services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Registers <see cref="HealthProbeFaviconAwareOptions"/> explicitly from the provided configuration and optionally applies an override.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">The configuration root containing a section named <c>HealthProbeOptions</c>.</param>
        /// <param name="manualConfigure">Optional delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
        public static IServiceCollection AddHealthProbeFaviconAware(this IServiceCollection services, IConfiguration configuration, Action<HealthProbeFaviconAwareOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);

            services.AddOptions<HealthProbeFaviconAwareOptions>().Bind(configuration.GetSection(nameof(HealthProbeFaviconAwareOptions)));
            if (manualConfigure != null) services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Adds shared registrations required by canonical redirect.
        /// </summary>
        /// <param name="services">The service collection.</param>
        private static void AddInfrastructure(IServiceCollection services)
        {
            services.TryAddSingleton(typeof(IDeferredLogger<>), typeof(DeferredLogger<>));
            services.AddOptions();
        }
    }
}