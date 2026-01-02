using System;

using Eigenverft.Routed.RequestFilters.GenericExtensions.IApplicationBuilderExtensions;
using Eigenverft.Routed.RequestFilters.GenericExtensions.IServiceProviderExtensions;
using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext;
using Eigenverft.Routed.RequestFilters.Options;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

            app.ApplicationServices.EnsureServicesRegistered(
                $"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddRequestSignatureFiltering)}().",
                typeof(IDeferredLogger<>));

            app.UseMiddlewareOnce<RemoteIpAddressContextMiddleware>();
            return app.UseMiddleware<RequestSignatureFiltering>();
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

            app.ApplicationServices.EnsureServicesRegistered(
                $"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddRequestSignatureFiltering)}().",
                typeof(IDeferredLogger<>));

            IOptionsMonitor<RequestSignatureFilteringOptions> innerOptionsMonitor =
                app.ApplicationServices.GetRequiredService<IOptionsMonitor<RequestSignatureFilteringOptions>>();

            var decoratedOptionsMonitor = new ConfiguredOptionsMonitor<RequestSignatureFilteringOptions>(innerOptionsMonitor, additionalConfigure);

            app.UseMiddlewareOnce<RemoteIpAddressContextMiddleware>();
            return app.UseMiddleware<RequestSignatureFiltering>(decoratedOptionsMonitor);
        }
    }
}
