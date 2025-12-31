using System;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Eigenverft.Routed.RequestFilters.Middleware.CanonicalHostRedirect
{
    /// <summary>
    /// Provides extension methods for configuring canonical host redirects.
    /// </summary>
    public static partial class IServiceCollectionExtensions
    {
        /// <summary>
        /// Registers canonical redirect with the standard behavior:
        /// binds from configuration section <c>CanonicalHostRedirectOptions</c> if present,
        /// otherwise uses defaults defined on <see cref="CanonicalHostRedirectOptions"/>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The updated service collection.</returns>
        /// <example>
        /// <code>
        /// builder.Services.AddCanonicalHostRedirect();
        /// </code>
        /// </example>
        public static IServiceCollection AddCanonicalHostRedirect(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);

            services.AddOptions<CanonicalHostRedirectOptions>().BindConfiguration(nameof(CanonicalHostRedirectOptions));

            return services;
        }

        /// <summary>
        /// Registers canonical redirect and applies additional code-based configuration on top of configuration binding.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="manualConfigure">Delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="manualConfigure"/> is null.</exception>
        public static IServiceCollection AddCanonicalHostRedirect(this IServiceCollection services, Action<CanonicalHostRedirectOptions> manualConfigure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manualConfigure);

            services.AddCanonicalHostRedirect();
            services.Configure(manualConfigure);

            return services;
        }

        /// <summary>
        /// Registers canonical redirect options explicitly from a provided configuration and optionally applies extra code-based configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration root that contains a section named <c>CanonicalHostRedirectOptions</c>.</param>
        /// <param name="manualConfigure">Optional delegate to modify or augment the bound configuration.</param>
        /// <returns>The updated service collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
        public static IServiceCollection AddCanonicalHostRedirect(this IServiceCollection services, IConfiguration configuration, Action<CanonicalHostRedirectOptions>? manualConfigure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            AddInfrastructure(services);

            services.AddOptions<CanonicalHostRedirectOptions>().Bind(configuration.GetSection(nameof(CanonicalHostRedirectOptions)));

            if (manualConfigure != null)
            {
                services.Configure(manualConfigure);
            }

            return services;
        }

        /// <summary>
        /// Adds shared registrations required by canonical redirect.
        /// </summary>
        /// <param name="services">The service collection.</param>
        private static void AddInfrastructure(IServiceCollection services)
        {
            services.TryAddSingleton(typeof(IDeferredLogger<>), typeof(DeferredLogger<>));
            services.AddOptions();
        }
    }
}
