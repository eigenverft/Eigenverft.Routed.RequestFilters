using System;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Eigenverft.Routed.RequestFilters.Hosting.WarmUpRequests
{
    /// <summary>
    /// Extension methods for registering warm-up requests.
    /// </summary>
    public static partial class IServiceCollectionExtensions
    {
        /// <summary>
        /// Registers warm-up requests hosted service and binds options from <c>WarmUpRequestsOptions</c>.
        /// </summary>
        /// <param name="services">Service collection.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddWarmUpRequests(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);

            services.AddOptions<WarmUpRequestsOptions>().BindConfiguration(nameof(WarmUpRequestsOptions));

            services.AddHttpClient(WarmUpRequestsOptions.HttpClientName);

            services.AddHostedService<WarmUpRequestsHostedService>();

            return services;
        }

        /// <summary>
        /// Registers warm-up requests hosted service and applies additional code-based configuration.
        /// </summary>
        /// <param name="services">Service collection.</param>
        /// <param name="manualConfigure">Delegate to configure <see cref="WarmUpRequestsOptions"/>.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddWarmUpRequests(this IServiceCollection services, Action<WarmUpRequestsOptions> manualConfigure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manualConfigure);

            services.AddWarmUpRequests();
            services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Registers warm-up requests hosted service and binds options from the provided configuration root.
        /// </summary>
        /// <param name="services">Service collection.</param>
        /// <param name="configuration">Configuration root containing a section named <c>WarmUpRequestsOptions</c>.</param>
        /// <param name="manualConfigure">Optional extra configuration.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddWarmUpRequests(this IServiceCollection services, IConfiguration configuration, Action<WarmUpRequestsOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);

            services.AddOptions<WarmUpRequestsOptions>().Bind(configuration.GetSection(nameof(WarmUpRequestsOptions)));

            if (manualConfigure != null)
            {
                services.Configure(manualConfigure);
            }

            services.AddHttpClient(WarmUpRequestsOptions.HttpClientName);

            services.AddHostedService<WarmUpRequestsHostedService>();

            return services;
        }

        private static void AddInfrastructure(IServiceCollection services)
        {
            services.TryAddSingleton(typeof(IDeferredLogger<>), typeof(DeferredLogger<>));
            services.AddOptions();
        }
    }
}
