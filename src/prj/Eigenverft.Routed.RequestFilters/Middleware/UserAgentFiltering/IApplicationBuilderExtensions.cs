using System;

using Eigenverft.WebLib.Middleware.Primitives.Infrastructure;
using Eigenverft.WebLib.ClientNetwork;
using Eigenverft.NetLib.Logging.Deferred;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.UserAgentFiltering
{
    /// <summary>
    /// Provides extension methods for registering <see cref="UserAgentFiltering"/> in the application's request pipeline.
    /// </summary>
    public static partial class IApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds <see cref="UserAgentFiltering"/> to the application's request pipeline.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <returns>The updated application builder.</returns>
        public static IApplicationBuilder UseUserAgentFiltering(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.ApplicationServices.EnsureServicesRegistered<IDeferredLogger<UserAgentFiltering>>(
                $"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddUserAgentFiltering)}().");

            app.UseClientNetworkFeature();
            return app.UseMiddlewareOnce<UserAgentFiltering>();
        }

        /// <summary>
        /// Adds <see cref="UserAgentFiltering"/> to the request pipeline while applying an additional configuration.
        /// The extra configuration is applied on top of the DI-registered options (which are auto-refreshed if appsettings change).
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <param name="additionalConfigure">Delegate to apply extra configuration to <see cref="UserAgentFilteringOptions"/>.</param>
        /// <returns>The updated application builder.</returns>
        public static IApplicationBuilder UseUserAgentFiltering(this IApplicationBuilder app, Action<UserAgentFilteringOptions> additionalConfigure)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(additionalConfigure);

            app.ApplicationServices.EnsureServicesRegistered<IDeferredLogger<UserAgentFiltering>>(
                $"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddUserAgentFiltering)}().");

            var decoratedOptionsMonitor = app.CreateUseSiteOptionsMonitor(additionalConfigure);

            app.UseClientNetworkFeature();
            return app.UseMiddleware<UserAgentFiltering>(decoratedOptionsMonitor);
        }
    }
}
