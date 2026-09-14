using System;

using Eigenverft.WebLib.Middleware.Primitives.Infrastructure;
using Eigenverft.WebLib.ClientNetwork;
using Eigenverft.NetLib.Logging.Deferred;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation.FilteringEvaluators;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.FilteringEvaluationGate
{
    /// <summary>
    /// Provides extension methods for registering <see cref="FilteringEvaluationGate"/> in the application's request pipeline.
    /// </summary>
    public static class IApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds <see cref="FilteringEvaluationGate"/> to the application's request pipeline.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <returns>The updated application builder.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="app"/> is null.</exception>
        public static IApplicationBuilder UseFilteringEvaluationGate(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.ApplicationServices.EnsureServicesRegistered($"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddFilteringEvaluationGate)}().", typeof(IDeferredLogger<FilteringEvaluationGate>));
            app.ApplicationServices.EnsureServicesRegistered($"Make sure to register a filtering evaluator via services.{nameof(FilteringEvaluatorServiceCollectionExtensions.AddFilteringEvaluator)}(...).", typeof(IFilteringEvaluationService));

            app.UseClientNetworkFeature();
            return app.UseMiddlewareOnce<FilteringEvaluationGate>();
        }

        /// <summary>
        /// Adds <see cref="FilteringEvaluationGate"/> to the request pipeline while applying an additional configuration.
        /// The extra configuration is applied on top of the DI-registered options (which are auto-refreshed if appsettings change).
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <param name="additionalConfigure">Delegate to apply extra configuration to <see cref="FilteringEvaluationGateOptions"/>.</param>
        /// <returns>The updated application builder.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="app"/> or <paramref name="additionalConfigure"/> is null.</exception>
        public static IApplicationBuilder UseFilteringEvaluationGate(this IApplicationBuilder app, Action<FilteringEvaluationGateOptions> additionalConfigure)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(additionalConfigure);

            app.ApplicationServices.EnsureServicesRegistered($"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddFilteringEvaluationGate)}().", typeof(IDeferredLogger<FilteringEvaluationGate>));
            app.ApplicationServices.EnsureServicesRegistered($"Make sure to register a filtering evaluator via services.{nameof(FilteringEvaluatorServiceCollectionExtensions.AddFilteringEvaluator)}(...).", typeof(IFilteringEvaluationService));

            var decoratedOptionsMonitor = app.CreateUseSiteOptionsMonitor(additionalConfigure);

            app.UseClientNetworkFeature();
            return app.UseMiddleware<FilteringEvaluationGate>(decoratedOptionsMonitor);
        }
    }
}
