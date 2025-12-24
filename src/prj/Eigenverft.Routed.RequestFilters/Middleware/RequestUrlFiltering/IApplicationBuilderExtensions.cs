using System;

using Eigenverft.Routed.RequestFilters.GenericExtensions.IApplicationBuilderExtensions;
using Eigenverft.Routed.RequestFilters.GenericExtensions.IServiceProviderExtensions;
using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext;
using Eigenverft.Routed.RequestFilters.Options;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

            app.ApplicationServices.EnsureServicesRegistered(
                $"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddRequestUrlFiltering)}().",
                typeof(IDeferredLogger<>));

            app.UseMiddlewareOnce<RemoteIpAddressContextMiddleware>();
            return app.UseMiddleware<RequestUrlFiltering>();
        }

        /// <summary>
        /// Adds <see cref="RequestUrlFiltering"/> to the request pipeline while applying an additional configuration.
        /// The extra configuration is applied on top of the DI-registered options (which are auto-refreshed if appsettings change).
        /// </summary>
        public static IApplicationBuilder UseRequestUrlFiltering(this IApplicationBuilder app, Action<RequestUrlFilteringOptions> additionalConfigure)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(additionalConfigure);

            app.ApplicationServices.EnsureServicesRegistered(
                $"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddRequestUrlFiltering)}().",
                typeof(IDeferredLogger<>));

            IOptionsMonitor<RequestUrlFilteringOptions> innerOptionsMonitor = app.ApplicationServices.GetRequiredService<IOptionsMonitor<RequestUrlFilteringOptions>>();
            var decoratedOptionsMonitor = new ConfiguredOptionsMonitor<RequestUrlFilteringOptions>(innerOptionsMonitor, additionalConfigure);

            app.UseMiddlewareOnce<RemoteIpAddressContextMiddleware>();
            return app.UseMiddleware<RequestUrlFiltering>(decoratedOptionsMonitor);
        }
    }
}
