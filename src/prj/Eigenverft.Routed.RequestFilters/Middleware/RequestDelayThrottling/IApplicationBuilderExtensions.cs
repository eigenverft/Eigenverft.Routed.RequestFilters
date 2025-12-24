using System;

using Eigenverft.Routed.RequestFilters.GenericExtensions.IApplicationBuilderExtensions;
using Eigenverft.Routed.RequestFilters.GenericExtensions.IServiceProviderExtensions;
using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation.FilteringEvaluators;

using Microsoft.AspNetCore.Builder;

namespace Eigenverft.Routed.RequestFilters.Middleware.RequestDelayThrottling
{
    /// <summary>
    /// Adds request delay throttling to the application's request pipeline.
    /// </summary>
    public static class IApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds <see cref="RequestDelayThrottling"/> to the application's request pipeline.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <returns>The updated application builder.</returns>
        public static IApplicationBuilder UseRequestDelayThrottling(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.ApplicationServices.EnsureServicesRegistered($"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddRequestDelayThrottling)}().", typeof(IDeferredLogger<>));

            // Optional, but keeps your ecosystem consistent (GetRemoteIpAddress()).
            app.UseMiddlewareOnce<RemoteIpAddressContextMiddleware>();

            return app.UseMiddleware<RequestDelayThrottling>();
        }
    }
}
