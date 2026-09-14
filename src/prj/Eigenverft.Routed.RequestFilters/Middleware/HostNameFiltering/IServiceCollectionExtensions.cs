using System;

using Eigenverft.NetLib.Logging.Deferred;
using Eigenverft.NetLib.Configuration.Binding;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.NullFiltering;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.HostNameFiltering
{
    /// <summary>
    /// Provides extension methods for configuring host name filtering.
    /// </summary>
    public static partial class IServiceCollectionExtensions
    {
        /// <summary>
        /// Registers host name filtering with the standard behavior:
        /// binds from configuration section <c>HostNameFilteringOptions</c> if present,
        /// otherwise uses defaults defined on <see cref="HostNameFilteringOptions"/>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The updated service collection.</returns>
        /// <example>
        /// <code>
        /// builder.Services.AddHostNameFiltering();
        /// </code>
        /// </example>
        public static IServiceCollection AddHostNameFiltering(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);

            services
                .AddOptions<HostNameFilteringOptions>()
                .BindReplacingCollectionDefaults(
                    nameof(HostNameFilteringOptions),
                    EmptyCollectionBehavior.UseCodeDefaults);

            return services;
        }

        /// <summary>
        /// Registers host name filtering and applies additional code-based configuration on top of configuration binding.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="manualConfigure">Delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="manualConfigure"/> is null.</exception>
        public static IServiceCollection AddHostNameFiltering(this IServiceCollection services, Action<HostNameFilteringOptions> manualConfigure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manualConfigure);

            services.AddHostNameFiltering();
            services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Registers host name filtering options explicitly from a provided configuration and optionally applies extra code-based configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration root that contains a section named <c>HostNameFilteringOptions</c>.</param>
        /// <param name="manualConfigure">Optional delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
        public static IServiceCollection AddHostNameFiltering(this IServiceCollection services, IConfiguration configuration, Action<HostNameFilteringOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);

            IConfigurationSection section = configuration.GetSection(nameof(HostNameFilteringOptions));
            services
                .AddOptions<HostNameFilteringOptions>()
                .Configure(options => section.BindReplacingCollectionDefaults(options, EmptyCollectionBehavior.UseCodeDefaults));
            services.AddSingleton<IOptionsChangeTokenSource<HostNameFilteringOptions>>(
                new ConfigurationChangeTokenSource<HostNameFilteringOptions>(Options.DefaultName, section));

            if (manualConfigure != null)
            {
                services.Configure(manualConfigure);
            }

            return services;
        }

        /// <summary>
        /// Adds shared registrations required by host name filtering.
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
