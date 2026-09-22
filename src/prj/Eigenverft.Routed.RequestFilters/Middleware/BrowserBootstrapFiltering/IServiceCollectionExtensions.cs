using System;

using Eigenverft.NetLib.Logging.Deferred;
using Eigenverft.NetLib.Configuration.Binding;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.NullFiltering;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.BrowserBootstrapFiltering
{
    /// <summary>
    /// Provides extension methods for configuring browser bootstrap filtering.
    /// </summary>
    public static partial class IServiceCollectionExtensions
    {
        /// <summary>
        /// Registers browser bootstrap filtering with standard behavior:
        /// binds from configuration section <c>BrowserBootstrapFilteringOptions</c> if present,
        /// otherwise uses defaults defined on <see cref="BrowserBootstrapFilteringOptions"/>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddBrowserBootstrapFiltering(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);

            services
                .AddOptions<BrowserBootstrapFilteringOptions>()
                .BindReplacingCollectionDefaults(
                    nameof(BrowserBootstrapFilteringOptions),
                    EmptyCollectionBehavior.UseCodeDefaults);

            return services;
        }

        /// <summary>
        /// Registers browser bootstrap filtering and applies additional code-based configuration on top of configuration binding.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="manualConfigure">Delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddBrowserBootstrapFiltering(this IServiceCollection services, Action<BrowserBootstrapFilteringOptions> manualConfigure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manualConfigure);

            services.AddBrowserBootstrapFiltering();
            services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Registers browser bootstrap filtering options explicitly from a provided configuration and optionally applies extra code-based configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration root that contains a section named <c>BrowserBootstrapFilteringOptions</c>.</param>
        /// <param name="manualConfigure">Optional delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddBrowserBootstrapFiltering(this IServiceCollection services, IConfiguration configuration, Action<BrowserBootstrapFilteringOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);

            IConfigurationSection section = configuration.GetSection(nameof(BrowserBootstrapFilteringOptions));
            services
                .AddOptions<BrowserBootstrapFilteringOptions>()
                .Configure(options => section.BindReplacingCollectionDefaults(options, EmptyCollectionBehavior.UseCodeDefaults));
            services.AddSingleton<IOptionsChangeTokenSource<BrowserBootstrapFilteringOptions>>(
                new ConfigurationChangeTokenSource<BrowserBootstrapFilteringOptions>(Options.DefaultName, section));

            if (manualConfigure != null)
            {
                services.Configure(manualConfigure);
            }

            return services;
        }

        private static void AddInfrastructure(IServiceCollection services)
        {
            services.AddDataProtection();
            services.TryAddSingleton(typeof(IDeferredLogger<>), typeof(DeferredLogger<>));
            services.TryAddSingleton<IFilteringEventStorage, NullFilteringEventStorage>();
            services.AddOptions();
        }
    }
}
