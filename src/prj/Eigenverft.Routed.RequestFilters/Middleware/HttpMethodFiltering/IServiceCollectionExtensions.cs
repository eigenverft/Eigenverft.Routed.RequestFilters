// ============================================================================
// File: IServiceCollectionExtensions.HttpMethodFiltering.cs
// Namespace: Eigenverft.Routed.RequestFilters.Middleware.HttpMethodFiltering
// ============================================================================

using System;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.NullFiltering;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Eigenverft.Routed.RequestFilters.Middleware.HttpMethodFiltering
{
    /// <summary>
    /// Provides extension methods for configuring http method filtering.
    /// </summary>
    public static partial class IServiceCollectionExtensions
    {
        /// <summary>
        /// Registers http method filtering with the standard behavior:
        /// binds from configuration section <c>HttpMethodFilteringOptions</c> if present,
        /// otherwise uses defaults defined on <see cref="HttpMethodFilteringOptions"/>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The updated service collection.</returns>
        /// <example>
        /// <code>
        /// builder.Services.AddHttpMethodFiltering();
        /// </code>
        /// </example>
        public static IServiceCollection AddHttpMethodFiltering(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);

            services
                .AddOptions<HttpMethodFilteringOptions>()
                .BindConfiguration(nameof(HttpMethodFilteringOptions));

            return services;
        }

        /// <summary>
        /// Registers http method filtering and applies additional code-based configuration on top of configuration binding.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="manualConfigure">Delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="manualConfigure"/> is null.</exception>
        public static IServiceCollection AddHttpMethodFiltering(this IServiceCollection services, Action<HttpMethodFilteringOptions> manualConfigure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manualConfigure);

            services.AddHttpMethodFiltering();
            services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Registers http method filtering options explicitly from a provided configuration and optionally applies extra code-based configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration root that contains a section named <c>HttpMethodFilteringOptions</c>.</param>
        /// <param name="manualConfigure">Optional delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
        public static IServiceCollection AddHttpMethodFiltering(this IServiceCollection services, IConfiguration configuration, Action<HttpMethodFilteringOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);

            services
                .AddOptions<HttpMethodFilteringOptions>()
                .Bind(configuration.GetSection(nameof(HttpMethodFilteringOptions)));

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
