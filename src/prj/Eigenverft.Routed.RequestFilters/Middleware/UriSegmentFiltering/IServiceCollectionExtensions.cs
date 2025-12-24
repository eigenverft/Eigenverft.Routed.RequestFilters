using System;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.NullFiltering;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Eigenverft.Routed.RequestFilters.Middleware.UriSegmentFiltering
{
    /// <summary>
    /// Provides extension methods for configuring URI segment filtering.
    /// </summary>
    public static partial class IServiceCollectionExtensions
    {
        /// <summary>
        /// Registers URI segment filtering with the standard behavior:
        /// binds from configuration section <c>UriSegmentFilteringOptions</c> if present,
        /// otherwise uses defaults defined on <see cref="UriSegmentFilteringOptions"/>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The updated service collection.</returns>
        /// <example>
        /// <code>
        /// builder.Services.AddUriSegmentFiltering();
        /// </code>
        /// </example>
        public static IServiceCollection AddUriSegmentFiltering(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);

            services.AddOptions<UriSegmentFilteringOptions>().BindConfiguration(nameof(UriSegmentFilteringOptions));

            return services;
        }

        /// <summary>
        /// Registers URI segment filtering and applies additional code-based configuration on top of configuration binding.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="manualConfigure">Delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="manualConfigure"/> is null.</exception>
        public static IServiceCollection AddUriSegmentFiltering(this IServiceCollection services, Action<UriSegmentFilteringOptions> manualConfigure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manualConfigure);

            services.AddUriSegmentFiltering();
            services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Registers URI segment filtering options explicitly from a provided configuration and optionally applies extra code-based configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration root that contains a section named <c>UriSegmentFilteringOptions</c>.</param>
        /// <param name="manualConfigure">Optional delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
        public static IServiceCollection AddUriSegmentFiltering(this IServiceCollection services, IConfiguration configuration, Action<UriSegmentFilteringOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);

            services.AddOptions<UriSegmentFilteringOptions>().Bind(configuration.GetSection(nameof(UriSegmentFilteringOptions)));

            if (manualConfigure != null)
            {
                services.Configure(manualConfigure);
            }

            return services;
        }

        private static void AddInfrastructure(IServiceCollection services)
        {
            services.TryAddSingleton(typeof(IDeferredLogger<>), typeof(DeferredLogger<>));
            services.TryAddSingleton<IFilteringEventStorage, NullFilteringEventStorage>();
            services.AddOptions();
        }
    }
}
