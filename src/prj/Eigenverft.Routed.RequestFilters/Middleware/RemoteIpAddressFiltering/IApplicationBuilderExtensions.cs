using System;

using Eigenverft.WebLib.Middleware.Primitives.Infrastructure;
using Eigenverft.WebLib.ClientNetwork;
using Eigenverft.NetLib.Logging.Deferred;

using Microsoft.AspNetCore.Builder;

namespace Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressFiltering
{
    /// <summary>
    /// Provides extension methods for registering <see cref="RemoteIpAddressFiltering"/> in the application's request pipeline.
    /// </summary>
    public static partial class IApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds <see cref="RemoteIpAddressFiltering"/> to the application's request pipeline.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <returns>The updated application builder.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="app"/> is null.</exception>
        public static IApplicationBuilder UseRemoteIpAddressFiltering(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.ApplicationServices.EnsureServicesRegistered($"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddRemoteIpAddressFiltering)}().", typeof(IDeferredLogger<RemoteIpAddressFiltering>));

            app.UseClientNetworkFeature();
            return app.UseMiddlewareOnce<RemoteIpAddressFiltering>();
        }

        /// <summary>
        /// Adds <see cref="RemoteIpAddressFiltering"/> to the request pipeline while applying an additional configuration.
        /// The extra configuration is applied on top of the DI-registered options (which are auto-refreshed if appsettings change).
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <param name="additionalConfigure">Delegate to apply extra configuration to <see cref="RemoteIpAddressFilteringOptions"/>.</param>
        /// <returns>The updated application builder.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="app"/> or <paramref name="additionalConfigure"/> is null.</exception>
        public static IApplicationBuilder UseRemoteIpAddressFiltering(this IApplicationBuilder app, Action<RemoteIpAddressFilteringOptions> additionalConfigure)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(additionalConfigure);

            app.ApplicationServices.EnsureServicesRegistered($"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddRemoteIpAddressFiltering)}().", typeof(IDeferredLogger<RemoteIpAddressFiltering>));

            var decoratedOptionsMonitor = app.CreateUseSiteOptionsMonitor(additionalConfigure);

            app.UseClientNetworkFeature();
            return app.UseMiddleware<RemoteIpAddressFiltering>(decoratedOptionsMonitor);
        }
    }
}
