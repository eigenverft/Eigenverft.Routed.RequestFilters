using System;
using System.IO;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
        /// Registers <see cref="StaticFileMiddleware" /> for a specific folder under the web root and prevents Razor Components fallthrough.
        /// </summary>
        /// <remarks>
        /// Requests to <c>/{folderName}/...</c> are handled in a dedicated branch. If no static file matches,
        /// the branch returns HTTP 404 and does not continue to Razor Components routing or SPA fallbacks.
        /// Place this before <c>app.MapRazorComponents&lt;TApp&gt;()</c> in the pipeline.
        ///
        /// If the target folder does not exist, the branch is still registered, but all requests under the path
        /// will immediately return HTTP 404 (so you never get a directory-not-found exception and never fall through).
        /// </remarks>
        /// <param name="app">The application builder.</param>
        /// <param name="folderName">Folder under <see cref="IWebHostEnvironment.WebRootPath" />.</param>
        /// <param name="configure">Optional callback to add or override mappings.</param>
        /// <param name="createDirectory">
        /// If <c>true</c>, creates the folder if missing. Default: <c>false</c>.
        /// Set to <c>true</c> if you expect the folder to be created on first run.
        /// </param>
        /// <returns>The same <paramref name="app" /> instance for chaining.</returns>
        /// <example>
        /// <code>
        /// // Serves "/uploads/*" from "{WebRootPath}/uploads/*"
        /// // Missing files under "/uploads" return 404 (no fallthrough to Razor Components).
        /// app.UseStaticFilesWithPwaAndBlazorContentTypes("uploads");
        /// </code>
        /// </example>
        public static IApplicationBuilder UseStaticFilesWithPwaAndBlazorContentTypes(
            this IApplicationBuilder app,
            string folderName,
            Action<FileExtensionContentTypeProvider>? configure = null,
            bool createDirectory = false)
        {
            ArgumentNullException.ThrowIfNull(app);

            if (string.IsNullOrWhiteSpace(folderName))
            {
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(folderName));
            }

            folderName = folderName.Trim().TrimStart('/').TrimEnd('/');

            if (string.IsNullOrWhiteSpace(folderName))
            {
                throw new ArgumentException("Folder name must not resolve to empty.", nameof(folderName));
            }

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

            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            // Block ".." traversal escaping the web root.
            if (!folderFull.StartsWith(webRootFull, pathComparison))
            {
                throw new InvalidOperationException("Folder resolves outside of the web root.");
            }

            var requestPath = new PathString("/" + folderName);

            var provider = new FileExtensionContentTypeProvider().AddPwaAndBlazorMappings();
            configure?.Invoke(provider);

            // Ensure the folder exists (optional). If it doesn't exist and we don't create it, we still
            // register the branch but make it "always 404" (no exceptions, no fallthrough).
            if (createDirectory)
            {
                Directory.CreateDirectory(folderFull);
            }

            var folderExists = Directory.Exists(folderFull);

            // Branch this subtree and make it terminal (no fallthrough into Razor Components).
            app.MapWhen(
                ctx => ctx.Request.Path.StartsWithSegments(requestPath),
                branch =>
                {
                    if (folderExists)
                    {
                        branch.UseStaticFiles(new StaticFileOptions
                        {
                            RequestPath = requestPath,
                            FileProvider = new PhysicalFileProvider(folderFull),
                            ContentTypeProvider = provider
                        });
                    }

                    // If the file wasn't served (or folder doesn't exist), return 404 and stop here.
                    branch.Run(ctx =>
                    {
                        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                        return Task.CompletedTask;
                    });
                });

            return app;
        }
    }
}
