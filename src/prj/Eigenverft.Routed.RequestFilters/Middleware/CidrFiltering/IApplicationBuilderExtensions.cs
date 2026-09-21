using System;

using Eigenverft.WebLib.Middleware.Primitives.Infrastructure;
using Eigenverft.WebLib.ClientNetwork;
using Eigenverft.NetLib.Logging.Deferred;

using Microsoft.AspNetCore.Builder;


namespace Eigenverft.Routed.RequestFilters.Middleware.CidrFiltering
{
    /// <summary>
    /// Provides extension methods for registering <see cref="CidrFiltering"/> in the application's request pipeline.
    /// </summary>
    public static partial class IApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds <see cref="CidrFiltering"/> to the application's request pipeline.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <returns>The updated application builder.</returns>
        public static IApplicationBuilder UseCidrFiltering(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.ApplicationServices.EnsureServicesRegistered($"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddCidrFiltering)}().", typeof(IDeferredLogger<CidrFiltering>));

            app.UseClientNetworkFeature();
            return app.UseMiddlewareOnce<CidrFiltering>();
        }

        /// <summary>
        /// Adds <see cref="CidrFiltering"/> to the request pipeline while applying an additional configuration.
        /// The extra configuration is applied on top of the DI-registered options (which are auto-refreshed if appsettings change).
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <param name="additionalConfigure">Delegate to apply extra configuration to <see cref="CidrFilteringOptions"/>.</param>
        /// <returns>The updated application builder.</returns>
        public static IApplicationBuilder UseCidrFiltering(this IApplicationBuilder app, Action<CidrFilteringOptions> additionalConfigure)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(additionalConfigure);

            app.ApplicationServices.EnsureServicesRegistered($"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddCidrFiltering)}().", typeof(IDeferredLogger<CidrFiltering>));

            var decoratedOptionsMonitor = app.CreateUseSiteOptionsMonitor(additionalConfigure);

            app.UseClientNetworkFeature();
            return app.UseMiddleware<CidrFiltering>(decoratedOptionsMonitor);
        }
    }
}
