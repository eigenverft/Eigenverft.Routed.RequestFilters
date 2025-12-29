using System;
using System.IO;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

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
        /// <param name="app">The application builder.</param>
        /// <param name="configure">Optional callback to add or override mappings.</param>
        /// <returns>The same <paramref name="app" /> instance for chaining.</returns>
        public static IApplicationBuilder UseStaticFilesWithPwaAndBlazorContentTypes(
            this IApplicationBuilder app,
            Action<FileExtensionContentTypeProvider>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(app);

            var provider = new FileExtensionContentTypeProvider().AddPwaAndBlazorMappings();
            configure?.Invoke(provider);

            return app.UseStaticFiles(new StaticFileOptions
            {
                ContentTypeProvider = provider
            });
        }

        /// <summary>
        /// Registers <see cref="StaticFileMiddleware" /> for a specific folder under the web root.
        /// </summary>
        /// <remarks>
        /// Serves <c>/{folderName}</c> from physical <c>{WebRootPath}/{folderName}</c>, including all subfolders.
        /// This is useful when you want mappings or options to apply only to a subtree.
        /// </remarks>
        /// <param name="app">The application builder.</param>
        /// <param name="folderName">Folder under <see cref="IWebHostEnvironment.WebRootPath" />.</param>
        /// <param name="configure">Optional callback to add or override mappings.</param>
        /// <returns>The same <paramref name="app" /> instance for chaining.</returns>
        /// <example>
        /// <code>
        /// app.UseStaticFilesWithPwaAndBlazorContentTypes("assets");
        /// // serves "/assets/*" from "{WebRootPath}/assets/*"
        /// </code>
        /// </example>
        public static IApplicationBuilder UseStaticFilesWithPwaAndBlazorContentTypes(
            this IApplicationBuilder app,
            string folderName,
            Action<FileExtensionContentTypeProvider>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(app);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(folderName));
            }

            folderName = folderName.Trim().TrimStart('/').TrimEnd('/');

            if (Path.IsPathRooted(folderName))
            {
                throw new ArgumentException("Expected a relative folder name under the web root.", nameof(folderName));
            }

            var env = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();
            if (string.IsNullOrWhiteSpace(env.WebRootPath))
            {
                throw new InvalidOperationException("WebRootPath is not set. Ensure a web root is configured.");
            }

            var webRootFull = Path.GetFullPath(env.WebRootPath);
            var folderFull = Path.GetFullPath(Path.Combine(webRootFull, folderName));

            // Block ".." traversal escaping the web root
            if (!folderFull.StartsWith(webRootFull, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Folder resolves outside of the web root.");
            }

            var provider = new FileExtensionContentTypeProvider().AddPwaAndBlazorMappings();
            configure?.Invoke(provider);

            return app.UseStaticFiles(new StaticFileOptions
            {
                RequestPath = "/" + folderName,
                FileProvider = new PhysicalFileProvider(folderFull),
                ContentTypeProvider = provider
            });
        }
    }
}
