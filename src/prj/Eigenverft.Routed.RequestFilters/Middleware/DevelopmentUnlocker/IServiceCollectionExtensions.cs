using System;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.NullFiltering;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Eigenverft.Routed.RequestFilters.Middleware.DevelopmentUnlocker
{
    /// <summary>
    /// Provides extension methods for configuring development unlock behavior.
    /// </summary>
    public static partial class IServiceCollectionExtensions
    {
        /// <summary>
        /// Registers development unlocker with the standard behavior:
        /// binds from configuration section <c>DevelopmentUnlockerOptions</c> if present,
        /// otherwise uses defaults defined on <see cref="DevelopmentUnlockerOptions"/>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The updated service collection.</returns>
        /// <example>
        /// <code>
        /// builder.Services.AddDevelopmentUnlocker();
        /// </code>
        /// </example>
        public static IServiceCollection AddDevelopmentUnlocker(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);

            services.AddOptions<DevelopmentUnlockerOptions>().BindConfiguration(nameof(DevelopmentUnlockerOptions));

            return services;
        }

        /// <summary>
        /// Registers development unlocker and applies additional code-based configuration on top of configuration binding.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="manualConfigure">Delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="manualConfigure"/> is null.</exception>
        public static IServiceCollection AddDevelopmentUnlocker(this IServiceCollection services, Action<DevelopmentUnlockerOptions> manualConfigure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manualConfigure);

            services.AddDevelopmentUnlocker();
            services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Registers development unlocker options explicitly from a provided configuration and optionally applies extra code-based configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration root that contains a section named <c>DevelopmentUnlockerOptions</c>.</param>
        /// <param name="manualConfigure">Optional delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
        public static IServiceCollection AddDevelopmentUnlocker(this IServiceCollection services, IConfiguration configuration, Action<DevelopmentUnlockerOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);

            services.AddOptions<DevelopmentUnlockerOptions>().Bind(configuration.GetSection(nameof(DevelopmentUnlockerOptions)));

            if (manualConfigure != null)
            {
                services.Configure(manualConfigure);
            }

            return services;
        }

        /// <summary>
        /// Adds shared registrations required by development unlocker.
        /// </summary>
        /// <param name="services">The service collection.</param>
        private static void AddInfrastructure(IServiceCollection services)
        {
            services.TryAddSingleton(typeof(IDeferredLogger<>), typeof(DeferredLogger<>));
            services.TryAddSingleton<IFilteringEventStorage, NullFilteringEventStorage>();
            services.AddOptions();
        }
    }
}
