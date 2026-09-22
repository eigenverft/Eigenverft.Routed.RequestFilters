using System;

using Eigenverft.WebLib.Middleware.Primitives.Infrastructure;
using Eigenverft.WebLib.ClientNetwork;
using Eigenverft.NetLib.Logging.Deferred;

using Microsoft.AspNetCore.Builder;

namespace Eigenverft.Routed.RequestFilters.Middleware.RequestUrlFiltering
{
    /// <summary>
    /// Provides extension methods for registering <see cref="RequestUrlFiltering"/> in the application's request pipeline.
    /// </summary>
    public static partial class IApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds <see cref="RequestUrlFiltering"/> to the application's request pipeline.
        /// </summary>
        public static IApplicationBuilder UseRequestUrlFiltering(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.ApplicationServices.EnsureServicesRegistered<IDeferredLogger<RequestUrlFiltering>>(
                $"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddRequestUrlFiltering)}().");

            app.UseClientNetworkFeature();
            return app.UseMiddlewareOnce<RequestUrlFiltering>();
        }

        /// <summary>
        /// Adds <see cref="RequestUrlFiltering"/> to the request pipeline while applying an additional configuration.
        /// The extra configuration is applied on top of the DI-registered options (which are auto-refreshed if appsettings change).
        /// </summary>
        public static IApplicationBuilder UseRequestUrlFiltering(this IApplicationBuilder app, Action<RequestUrlFilteringOptions> additionalConfigure)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(additionalConfigure);

            app.ApplicationServices.EnsureServicesRegistered<IDeferredLogger<RequestUrlFiltering>>(
                $"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddRequestUrlFiltering)}().");

            var decoratedOptionsMonitor = app.CreateUseSiteOptionsMonitor(additionalConfigure);

            app.UseClientNetworkFeature();
            return app.UseMiddleware<RequestUrlFiltering>(decoratedOptionsMonitor);
        }
    }
}
