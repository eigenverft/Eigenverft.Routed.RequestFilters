using System;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.NullFiltering;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Eigenverft.Routed.RequestFilters.Middleware.PathDepthFiltering
{
    /// <summary>
    /// Provides extension methods for configuring path depth filtering.
    /// </summary>
    public static partial class IServiceCollectionExtensions
    {
        /// <summary>
        /// Registers path depth filtering with the standard behavior:
        /// binds from configuration section <c>PathDepthFilteringOptions</c> if present,
        /// otherwise uses defaults defined on <see cref="PathDepthFilteringOptions"/>.
        /// </summary>
        public static IServiceCollection AddPathDepthFiltering(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);

            services.AddOptions<PathDepthFilteringOptions>().BindConfiguration(nameof(PathDepthFilteringOptions));

            return services;
        }

        /// <summary>
        /// Registers path depth filtering and applies additional code-based configuration on top of configuration binding.
        /// </summary>
        public static IServiceCollection AddPathDepthFiltering(this IServiceCollection services, Action<PathDepthFilteringOptions> manualConfigure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manualConfigure);

            services.AddPathDepthFiltering();
            services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Registers path depth filtering options explicitly from a provided configuration and optionally applies extra code-based configuration.
        /// </summary>
        public static IServiceCollection AddPathDepthFiltering(this IServiceCollection services, IConfiguration configuration, Action<PathDepthFilteringOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);

            services.AddOptions<PathDepthFilteringOptions>().Bind(configuration.GetSection(nameof(PathDepthFilteringOptions)));

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
