using System;

using Eigenverft.WebLib.Middleware.Primitives.Infrastructure;
using Eigenverft.WebLib.ClientNetwork;
using Eigenverft.NetLib.Logging.Deferred;

using Microsoft.AspNetCore.Builder;

namespace Eigenverft.Routed.RequestFilters.Middleware.PathDepthFiltering
{
    /// <summary>
    /// Provides extension methods for registering <see cref="PathDepthFiltering"/> in the application's request pipeline.
    /// </summary>
    public static partial class IApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds <see cref="PathDepthFiltering"/> to the application's request pipeline.
        /// </summary>
        public static IApplicationBuilder UsePathDepthFiltering(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.ApplicationServices.EnsureServicesRegistered($"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddPathDepthFiltering)}().", typeof(IDeferredLogger<PathDepthFiltering>));

            app.UseClientNetworkFeature();
            return app.UseMiddlewareOnce<PathDepthFiltering>();
        }

        /// <summary>
        /// Adds <see cref="PathDepthFiltering"/> to the request pipeline while applying an additional configuration.
        /// The extra configuration is applied on top of the DI-registered options (which are auto-refreshed if appsettings change).
        /// </summary>
        public static IApplicationBuilder UsePathDepthFiltering(this IApplicationBuilder app, Action<PathDepthFilteringOptions> additionalConfigure)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(additionalConfigure);

            app.ApplicationServices.EnsureServicesRegistered($"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddPathDepthFiltering)}().", typeof(IDeferredLogger<PathDepthFiltering>));

            var decoratedOptionsMonitor = app.CreateUseSiteOptionsMonitor(additionalConfigure);

            app.UseClientNetworkFeature();
            return app.UseMiddleware<PathDepthFiltering>(decoratedOptionsMonitor);
        }
    }
}
