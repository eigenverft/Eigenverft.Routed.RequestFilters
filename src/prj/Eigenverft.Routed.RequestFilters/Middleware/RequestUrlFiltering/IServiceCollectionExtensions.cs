using System;

using Eigenverft.NetLib.Logging.Deferred;
using Eigenverft.NetLib.Configuration.Binding;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.NullFiltering;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.RequestUrlFiltering
{
    /// <summary>
    /// Provides extension methods for configuring request URL path filtering.
    /// </summary>
    public static partial class IServiceCollectionExtensions
    {
        /// <summary>
        /// Registers request URL path filtering with the standard behavior:
        /// binds from configuration section <c>RequestUrlFilteringOptions</c> if present,
        /// otherwise uses defaults defined on <see cref="RequestUrlFilteringOptions"/>.
        /// </summary>
        public static IServiceCollection AddRequestUrlFiltering(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);

            services
                .AddOptions<RequestUrlFilteringOptions>()
                .BindReplacingCollectionDefaults(
                    nameof(RequestUrlFilteringOptions),
                    EmptyCollectionBehavior.UseCodeDefaults);

            return services;
        }

        /// <summary>
        /// Registers request URL path filtering and applies additional code-based configuration on top of configuration binding.
        /// </summary>
        public static IServiceCollection AddRequestUrlFiltering(this IServiceCollection services, Action<RequestUrlFilteringOptions> manualConfigure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manualConfigure);

            services.AddRequestUrlFiltering();
            services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Registers request URL path filtering options explicitly from a provided configuration and optionally applies extra code-based configuration.
        /// </summary>
        public static IServiceCollection AddRequestUrlFiltering(this IServiceCollection services, IConfiguration configuration, Action<RequestUrlFilteringOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);

            IConfigurationSection section = configuration.GetSection(nameof(RequestUrlFilteringOptions));
            services
                .AddOptions<RequestUrlFilteringOptions>()
                .Configure(options => section.BindReplacingCollectionDefaults(options, EmptyCollectionBehavior.UseCodeDefaults));
            services.AddSingleton<IOptionsChangeTokenSource<RequestUrlFilteringOptions>>(
                new ConfigurationChangeTokenSource<RequestUrlFilteringOptions>(Options.DefaultName, section));

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
