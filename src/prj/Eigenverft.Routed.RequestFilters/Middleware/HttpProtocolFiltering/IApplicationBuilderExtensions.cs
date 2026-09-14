using System;

using Eigenverft.WebLib.Middleware.Primitives.Infrastructure;
using Eigenverft.WebLib.ClientNetwork;
using Eigenverft.NetLib.Logging.Deferred;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation.FilteringEvaluators;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.HttpProtocolFiltering
{
    /// <summary>
    /// Provides extension methods for registering <see cref="HttpProtocolFiltering"/> in the application's request pipeline.
    /// </summary>
    public static partial class IApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds <see cref="HttpProtocolFiltering"/> to the application's request pipeline.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <returns>The updated application builder.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="app"/> is null.</exception>
        public static IApplicationBuilder UseHttpProtocolFiltering(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.ApplicationServices.EnsureServicesRegistered($"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddHttpProtocolFiltering)}().", typeof(IDeferredLogger<HttpProtocolFiltering>));

            app.UseClientNetworkFeature();
            return app.UseMiddlewareOnce<HttpProtocolFiltering>();
        }

        /// <summary>
        /// Adds <see cref="HttpProtocolFiltering"/> to the request pipeline while applying an additional configuration.
        /// The extra configuration is applied on top of the DI-registered options (which are auto-refreshed if appsettings change).
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <param name="additionalConfigure">Delegate to apply extra configuration to <see cref="HttpProtocolFilteringOptions"/>.</param>
        /// <returns>The updated application builder.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="app"/> or <paramref name="additionalConfigure"/> is null.</exception>
        public static IApplicationBuilder UseHttpProtocolFiltering(this IApplicationBuilder app, Action<HttpProtocolFilteringOptions> additionalConfigure)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(additionalConfigure);

            app.ApplicationServices.EnsureServicesRegistered($"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddHttpProtocolFiltering)}().", typeof(IDeferredLogger<HttpProtocolFiltering>));

            var decoratedOptionsMonitor = app.CreateUseSiteOptionsMonitor(additionalConfigure);

            app.UseClientNetworkFeature();
            return app.UseMiddleware<HttpProtocolFiltering>(decoratedOptionsMonitor);
        }
    }
}
