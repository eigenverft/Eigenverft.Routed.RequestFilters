using System;

using Eigenverft.Routed.RequestFilters.GenericExtensions.IApplicationBuilderExtensions;
using Eigenverft.Routed.RequestFilters.GenericExtensions.IServiceProviderExtensions;
using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.AspNetCore.Builder;

namespace Eigenverft.Routed.RequestFilters.Middleware.BrowserBootstrapFiltering
{
    /// <summary>
    /// Provides extension methods for registering <see cref="BrowserBootstrapFiltering"/> in the application's request pipeline.
    /// </summary>
    public static partial class IApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds <see cref="BrowserBootstrapFiltering"/> to the application's request pipeline.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <returns>The updated application builder.</returns>
        /// <remarks>
        /// Place this before <c>UseDefaultFiles</c> and <c>UseStaticFiles</c> so the initial index request can be bootstrapped.
        /// </remarks>
        public static IApplicationBuilder UseBrowserBootstrapFiltering(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.ApplicationServices.EnsureServicesRegistered(
                $"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddBrowserBootstrapFiltering)}().",
                typeof(IDeferredLogger<>));

            app.UseMiddlewareOnce<RemoteIpAddressContextMiddleware>();
            return app.UseMiddleware<BrowserBootstrapFiltering>();
        }
    }
}
