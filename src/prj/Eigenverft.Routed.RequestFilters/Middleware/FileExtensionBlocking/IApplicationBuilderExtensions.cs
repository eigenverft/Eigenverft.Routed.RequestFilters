using System;

using Eigenverft.Routed.RequestFilters.GenericExtensions.IApplicationBuilderExtensions;
using Eigenverft.Routed.RequestFilters.GenericExtensions.IServiceProviderExtensions;
using Eigenverft.Routed.RequestFilters.Options;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.FileExtensionBlocking
{
    /// <summary>
    /// Provides extension methods for registering <see cref="FileExtensionBlocking"/> in the application's request pipeline.
    /// </summary>
    public static partial class IApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds <see cref="FileExtensionBlocking"/> to the application's request pipeline.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <returns>The updated application builder.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="app"/> is null.</exception>
        public static IApplicationBuilder UseFileExtensionBlocking(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.ApplicationServices.EnsureServicesRegistered(
                $"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddFileExtensionBlocking)}().",
                typeof(IDeferredLogger<>));

            return app.UseMiddleware<FileExtensionBlocking>();
        }

        /// <summary>
        /// Adds <see cref="FileExtensionBlocking"/> to the request pipeline while applying an additional configuration.
        /// The extra configuration is applied on top of the DI-registered options (which are auto-refreshed if appsettings change).
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <param name="additionalConfigure">Delegate to apply extra configuration to <see cref="FileExtensionBlockingOptions"/>.</param>
        /// <returns>The updated application builder.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="app"/> or <paramref name="additionalConfigure"/> is null.</exception>
        public static IApplicationBuilder UseFileExtensionBlocking(this IApplicationBuilder app, Action<FileExtensionBlockingOptions> additionalConfigure)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(additionalConfigure);

            app.ApplicationServices.EnsureServicesRegistered(
                $"Make sure to register deferred logging via services.{nameof(IServiceCollectionExtensions.AddFileExtensionBlocking)}().",
                typeof(IDeferredLogger<>));

            IOptionsMonitor<FileExtensionBlockingOptions> innerOptionsMonitor = app.ApplicationServices.GetRequiredService<IOptionsMonitor<FileExtensionBlockingOptions>>();
            var decoratedOptionsMonitor = new ConfiguredOptionsMonitor<FileExtensionBlockingOptions>(innerOptionsMonitor, additionalConfigure);

            return app.UseMiddleware<FileExtensionBlocking>(decoratedOptionsMonitor);
        }
    }
}
