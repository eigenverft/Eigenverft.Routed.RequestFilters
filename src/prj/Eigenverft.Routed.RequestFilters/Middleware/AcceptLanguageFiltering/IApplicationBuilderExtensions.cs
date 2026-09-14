using System;

using Eigenverft.WebLib.Middleware.Primitives.Infrastructure;
using Eigenverft.WebLib.ClientNetwork;
using Eigenverft.NetLib.Logging.Deferred;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation.FilteringEvaluators;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.AcceptLanguageFiltering
{
    /// <summary>
    /// Provides extension methods for registering <see cref="AcceptLanguageFiltering"/> in the application's request pipeline.
    /// </summary>
    public static partial class IApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds <see cref="AcceptLanguageFiltering"/> to the application's request pipeline.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <returns>The updated application builder.</returns>
        public static IApplicationBuilder UseAcceptLanguageFiltering(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.ApplicationServices.EnsureServicesRegistered($"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddAcceptLanguageFiltering)}().", typeof(IDeferredLogger<AcceptLanguageFiltering>));

            app.UseClientNetworkFeature();
            return app.UseMiddlewareOnce<AcceptLanguageFiltering>();
        }

        /// <summary>
        /// Adds <see cref="AcceptLanguageFiltering"/> to the request pipeline while applying an additional configuration.
        /// The extra configuration is applied on top of the DI-registered options (which are auto-refreshed if appsettings change).
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <param name="additionalConfigure">Delegate to apply extra configuration to <see cref="AcceptLanguageFilteringOptions"/>.</param>
        /// <returns>The updated application builder.</returns>
        public static IApplicationBuilder UseAcceptLanguageFiltering(this IApplicationBuilder app, Action<AcceptLanguageFilteringOptions> additionalConfigure)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(additionalConfigure);

            app.ApplicationServices.EnsureServicesRegistered($"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddAcceptLanguageFiltering)}().", typeof(IDeferredLogger<AcceptLanguageFiltering>));

            var decoratedOptionsMonitor = app.CreateUseSiteOptionsMonitor(additionalConfigure);

            app.UseClientNetworkFeature();
            return app.UseMiddleware<AcceptLanguageFiltering>(decoratedOptionsMonitor);
        }
    }
}
