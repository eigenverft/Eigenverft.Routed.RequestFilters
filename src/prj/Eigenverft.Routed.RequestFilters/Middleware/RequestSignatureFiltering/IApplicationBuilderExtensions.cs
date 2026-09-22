using System;

using Eigenverft.WebLib.Middleware.Primitives.Infrastructure;
using Eigenverft.WebLib.ClientNetwork;
using Eigenverft.NetLib.Logging.Deferred;

using Microsoft.AspNetCore.Builder;

namespace Eigenverft.Routed.RequestFilters.Middleware.RequestSignatureFiltering
{
    /// <summary>
    /// Provides extension methods for registering <see cref="RequestSignatureFiltering"/> in the application's request pipeline.
    /// </summary>
    public static partial class IApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds <see cref="RequestSignatureFiltering"/> to the application's request pipeline.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <returns>The updated application builder.</returns>
        public static IApplicationBuilder UseRequestSignatureFiltering(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.ApplicationServices.EnsureServicesRegistered<IDeferredLogger<RequestSignatureFiltering>>(
                $"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddRequestSignatureFiltering)}().");

            app.UseClientNetworkFeature();
            return app.UseMiddlewareOnce<RequestSignatureFiltering>();
        }

        /// <summary>
        /// Adds <see cref="RequestSignatureFiltering"/> to the request pipeline while applying an additional configuration.
        /// The extra configuration is applied on top of the DI-registered options (which are auto-refreshed if appsettings change).
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <param name="additionalConfigure">Delegate to apply extra configuration to <see cref="RequestSignatureFilteringOptions"/>.</param>
        /// <returns>The updated application builder.</returns>
        public static IApplicationBuilder UseRequestSignatureFiltering(this IApplicationBuilder app, Action<RequestSignatureFilteringOptions> additionalConfigure)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(additionalConfigure);

            app.ApplicationServices.EnsureServicesRegistered<IDeferredLogger<RequestSignatureFiltering>>(
                $"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddRequestSignatureFiltering)}().");

            var decoratedOptionsMonitor = app.CreateUseSiteOptionsMonitor(additionalConfigure);

            app.UseClientNetworkFeature();
            return app.UseMiddleware<RequestSignatureFiltering>(decoratedOptionsMonitor);
        }
    }
}
