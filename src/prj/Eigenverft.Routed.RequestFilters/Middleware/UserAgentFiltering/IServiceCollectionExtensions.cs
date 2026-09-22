using System;

using Eigenverft.NetLib.Logging.Deferred;
using Eigenverft.NetLib.Configuration.Binding;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.NullFiltering;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.UserAgentFiltering
{
    /// <summary>
    /// Provides extension methods for configuring User-Agent filtering.
    /// </summary>
    public static partial class IServiceCollectionExtensions
    {
        /// <summary>
        /// Registers User-Agent filtering with the standard behavior:
        /// binds from configuration section <c>UserAgentFilteringOptions</c> if present,
        /// otherwise uses defaults defined on <see cref="UserAgentFilteringOptions"/>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddUserAgentFiltering(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);

            services
                .AddOptions<UserAgentFilteringOptions>()
                .BindReplacingCollectionDefaults(
                    nameof(UserAgentFilteringOptions),
                    EmptyCollectionBehavior.UseCodeDefaults);

            return services;
        }

        /// <summary>
        /// Registers User-Agent filtering and applies additional code-based configuration on top of configuration binding.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="manualConfigure">Delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddUserAgentFiltering(this IServiceCollection services, Action<UserAgentFilteringOptions> manualConfigure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manualConfigure);

            services.AddUserAgentFiltering();
            services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Registers User-Agent filtering options explicitly from a provided configuration and optionally applies extra code-based configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration root that contains a section named <c>UserAgentFilteringOptions</c>.</param>
        /// <param name="manualConfigure">Optional delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddUserAgentFiltering(this IServiceCollection services, IConfiguration configuration, Action<UserAgentFilteringOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);

            IConfigurationSection section = configuration.GetSection(nameof(UserAgentFilteringOptions));
            services
                .AddOptions<UserAgentFilteringOptions>()
                .Configure(options => section.BindReplacingCollectionDefaults(options, EmptyCollectionBehavior.UseCodeDefaults));
            services.AddSingleton<IOptionsChangeTokenSource<UserAgentFilteringOptions>>(
                new ConfigurationChangeTokenSource<UserAgentFilteringOptions>(Options.DefaultName, section));

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
