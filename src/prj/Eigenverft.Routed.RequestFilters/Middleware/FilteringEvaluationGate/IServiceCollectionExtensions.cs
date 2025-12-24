using System;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.NullFiltering;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Eigenverft.Routed.RequestFilters.Middleware.FilteringEvaluationGate
{
    /// <summary>
    /// Provides extension methods for registering <see cref="FilteringEvaluationGate"/> and its options.
    /// </summary>
    public static class IServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="FilteringEvaluationGateOptions"/> using the standard behavior:
        /// binds from configuration section <c>FilteringEvaluationGateOptions</c> if present,
        /// otherwise uses defaults defined on <see cref="FilteringEvaluationGateOptions"/>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The updated service collection.</returns>
        /// <example>
        /// <code>
        /// builder.Services.AddFilteringEvaluationGate();
        /// </code>
        /// </example>
        public static IServiceCollection AddFilteringEvaluationGate(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);

            // Binds from IConfiguration in DI. If appsettings is not loaded or the section is missing,
            // binding is a no-op and property initializer defaults remain in effect.
            services.AddOptions<FilteringEvaluationGateOptions>().BindConfiguration(nameof(FilteringEvaluationGateOptions));

            return services;
        }

        /// <summary>
        /// Registers <see cref="FilteringEvaluationGateOptions"/> and applies additional code-based configuration.
        /// The extra configuration is applied on top of configuration binding.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="manualConfigure">Delegate for configuring <see cref="FilteringEvaluationGateOptions"/>.</param>
        /// <returns>The updated service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="manualConfigure"/> is null.</exception>
        /// <example>
        /// <code>
        /// builder.Services.AddFilteringEvaluationGate(o =&gt; o.AllowBlockedRequests = true);
        /// </code>
        /// </example>
        public static IServiceCollection AddFilteringEvaluationGate(this IServiceCollection services, Action<FilteringEvaluationGateOptions> manualConfigure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manualConfigure);

            services.AddFilteringEvaluationGate();
            services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Registers <see cref="FilteringEvaluationGateOptions"/> explicitly from a provided configuration
        /// and optionally applies additional code-based configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">The configuration root containing a section named <c>FilteringEvaluationGateOptions</c>.</param>
        /// <param name="manualConfigure">Optional delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
        public static IServiceCollection AddFilteringEvaluationGate(this IServiceCollection services, IConfiguration configuration, Action<FilteringEvaluationGateOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);

            services.AddOptions<FilteringEvaluationGateOptions>().Bind(configuration.GetSection(nameof(FilteringEvaluationGateOptions)));

            if (manualConfigure != null)
            {
                services.Configure(manualConfigure);
            }

            return services;
        }

        /// <summary>
        /// Adds shared registrations required by the evaluation gate.
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
