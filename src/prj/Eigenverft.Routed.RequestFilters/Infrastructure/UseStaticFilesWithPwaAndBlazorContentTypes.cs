using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;

namespace Eigenverft.Routed.RequestFilters.Infrastructure
{
    /// <summary>
    /// Extensions for static file content type mappings commonly needed for PWA and Blazor assets.
    /// </summary>
    public static partial class IApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds essential PWA and Blazor-related mappings to an existing <see cref="FileExtensionContentTypeProvider" />.
        /// </summary>
        /// <remarks>
        /// Ensures <c>application/manifest+json</c> for <c>.webmanifest</c> and uses <c>application/octet-stream</c>
        /// for common binary artifacts such as <c>.br</c> and <c>.dat</c>.
        /// </remarks>
        /// <param name="provider">The provider to extend.</param>
        /// <returns>The same <paramref name="provider" /> instance for chaining.</returns>
        /// <example>
        /// <code>
        /// var provider = new FileExtensionContentTypeProvider()
        ///     .AddPwaAndBlazorMappings();
        /// </code>
        /// </example>
        public static FileExtensionContentTypeProvider AddPwaAndBlazorMappings(this FileExtensionContentTypeProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            provider.Mappings[".webmanifest"] = "application/manifest+json";
            provider.Mappings[".br"] = "application/octet-stream";
            provider.Mappings[".dat"] = "application/octet-stream";

            return provider;
        }

        /// <summary>
        /// Registers <see cref="StaticFileMiddleware" /> with a content type provider that includes PWA and Blazor mappings.
        /// </summary>
        /// <remarks>
        /// This is mainly useful when you have static web assets that include extensions not covered by the default provider.
        /// You can further customize mappings via <paramref name="configure" />.
        /// </remarks>
        /// <param name="app">The application builder.</param>
        /// <param name="configure">Optional callback to add or override mappings.</param>
        /// <returns>The same <paramref name="app" /> instance for chaining.</returns>
        /// <example>
        /// <code>
        /// app.UseStaticFilesWithPwaAndBlazorContentTypes(p =&gt;
        /// {
        ///     p.Mappings[".dll"] = "application/octet-stream";
        /// });
        /// </code>
        /// </example>
        public static IApplicationBuilder UseStaticFilesWithPwaAndBlazorContentTypes(this IApplicationBuilder app, Action<FileExtensionContentTypeProvider>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(app);

            var provider = new FileExtensionContentTypeProvider().AddPwaAndBlazorMappings();

            configure?.Invoke(provider);

            return app.UseStaticFiles(new StaticFileOptions
            {
                ContentTypeProvider = provider
            });
        }
    }
}