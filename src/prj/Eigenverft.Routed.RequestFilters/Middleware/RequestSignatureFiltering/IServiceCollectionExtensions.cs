using System;

using Eigenverft.NetLib.Logging.Deferred;
using Eigenverft.NetLib.Configuration.Binding;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.NullFiltering;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.RequestSignatureFiltering
{
    /// <summary>
    /// Provides extension methods for configuring request signature filtering.
    /// </summary>
    public static partial class IServiceCollectionExtensions
    {
        /// <summary>
        /// Registers request signature filtering with the standard behavior:
        /// binds from configuration section <c>RequestSignatureFilteringOptions</c> if present,
        /// otherwise uses defaults defined on <see cref="RequestSignatureFilteringOptions"/>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddRequestSignatureFiltering(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);

            services
                .AddOptions<RequestSignatureFilteringOptions>()
                .BindReplacingCollectionDefaults(
                    nameof(RequestSignatureFilteringOptions),
                    EmptyCollectionBehavior.UseCodeDefaults);

            return services;
        }

        /// <summary>
        /// Registers request signature filtering and applies additional code-based configuration on top of configuration binding.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="manualConfigure">Delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddRequestSignatureFiltering(this IServiceCollection services, Action<RequestSignatureFilteringOptions> manualConfigure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manualConfigure);

            services.AddRequestSignatureFiltering();
            services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Registers request signature filtering options explicitly from a provided configuration and optionally applies extra code-based configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration root that contains a section named <c>RequestSignatureFilteringOptions</c>.</param>
        /// <param name="manualConfigure">Optional delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddRequestSignatureFiltering(this IServiceCollection services, IConfiguration configuration, Action<RequestSignatureFilteringOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);

            IConfigurationSection section = configuration.GetSection(nameof(RequestSignatureFilteringOptions));
            services
                .AddOptions<RequestSignatureFilteringOptions>()
                .Configure(options => section.BindReplacingCollectionDefaults(options, EmptyCollectionBehavior.UseCodeDefaults));
            services.AddSingleton<IOptionsChangeTokenSource<RequestSignatureFilteringOptions>>(
                new ConfigurationChangeTokenSource<RequestSignatureFilteringOptions>(Options.DefaultName, section));

            if (manualConfigure != null)
            {
                services.Configure(manualConfigure);
            }

            return services;
        }

        /// <summary>
        /// Adds shared registrations required by request signature filtering.
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
