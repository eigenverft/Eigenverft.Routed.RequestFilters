using System;

using Eigenverft.Routed.RequestFilters.GenericExtensions.IApplicationBuilderExtensions;
using Eigenverft.Routed.RequestFilters.GenericExtensions.IServiceProviderExtensions;
using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext;
using Eigenverft.Routed.RequestFilters.Options;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.DevelopmentUnlocker
{
    /// <summary>
    /// Provides extension methods for registering <see cref="DevelopmentUnlocker"/> in the application's request pipeline.
    /// </summary>
    public static partial class IApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds <see cref="DevelopmentUnlocker"/> to the application's request pipeline.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <returns>The updated application builder.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="app"/> is null.</exception>
        public static IApplicationBuilder UseDevelopmentUnlocker(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.ApplicationServices.EnsureServicesRegistered(
                $"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddDevelopmentUnlocker)}().",
                typeof(IDeferredLogger<>));

            app.UseMiddlewareOnce<RemoteIpAddressContextMiddleware>();
            return app.UseMiddleware<DevelopmentUnlocker>();
        }

        /// <summary>
        /// Adds <see cref="DevelopmentUnlocker"/> to the request pipeline while applying an additional configuration.
        /// The extra configuration is applied on top of the DI-registered options (which are auto-refreshed if appsettings change).
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <param name="additionalConfigure">Delegate to apply extra configuration to <see cref="DevelopmentUnlockerOptions"/>.</param>
        /// <returns>The updated application builder.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="app"/> or <paramref name="additionalConfigure"/> is null.</exception>
        public static IApplicationBuilder UseDevelopmentUnlocker(this IApplicationBuilder app, Action<DevelopmentUnlockerOptions> additionalConfigure)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(additionalConfigure);

            app.ApplicationServices.EnsureServicesRegistered(
                $"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddDevelopmentUnlocker)}().",
                typeof(IDeferredLogger<>));

            IOptionsMonitor<DevelopmentUnlockerOptions> innerOptionsMonitor = app.ApplicationServices.GetRequiredService<IOptionsMonitor<DevelopmentUnlockerOptions>>();
            var decoratedOptionsMonitor = new ConfiguredOptionsMonitor<DevelopmentUnlockerOptions>(innerOptionsMonitor, additionalConfigure);

            app.UseMiddlewareOnce<RemoteIpAddressContextMiddleware>();
            return app.UseMiddleware<DevelopmentUnlocker>(decoratedOptionsMonitor);
        }
    }
}
