using System;

using Eigenverft.Routed.RequestFilters.GenericExtensions.IApplicationBuilderExtensions;
using Eigenverft.Routed.RequestFilters.GenericExtensions.IServiceProviderExtensions;
using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.AspNetCore.Builder;

namespace Eigenverft.Routed.RequestFilters.Middleware.RequestRateSmoothing
{
    /// <summary>
    /// Adds request rate smoothing to the application's request pipeline.
    /// </summary>
    public static class IApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds <see cref="RequestRateSmoothing"/> to the application's request pipeline.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <returns>The updated application builder.</returns>
        /// <example>
        /// <code>
        /// app.UseRequestRateSmoothing();
        /// </code>
        /// </example>
        public static IApplicationBuilder UseRequestRateSmoothing(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.ApplicationServices.EnsureServicesRegistered(
                $"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddRequestRateSmoothing)}().",
                typeof(IDeferredLogger<>));

            // Optional, but keeps your ecosystem consistent (GetRemoteIpAddress()).
            app.UseMiddlewareOnce<RemoteIpAddressContextMiddleware>();

            return app.UseMiddleware<RequestRateSmoothing>();
        }
    }
}
