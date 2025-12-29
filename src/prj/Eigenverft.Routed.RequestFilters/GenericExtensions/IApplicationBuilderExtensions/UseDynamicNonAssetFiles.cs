using System;
using System.IO;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Eigenverft.Routed.RequestFilters.GenericExtensions.IApplicationBuilderExtensions
{
    /// <summary>
    /// Middleware helpers for serving a mounted static-only folder before endpoint fallbacks
    /// (for example before Razor Components / Blazor fallbacks).
    /// </summary>
    public static class NonAssetFilesExtensions
    {
        /// <summary>
        /// Serves a static-only folder mounted at <c>/dynamic</c> from <c>wwwroot/dynamic</c>.
        /// </summary>
        /// <remarks>
        /// This branch is terminal: if a file is not served, it returns <c>404</c> and stops.
        /// Deep links are rewritten to the mount directory so DefaultFiles can serve <c>index.html</c>.
        /// </remarks>
        /// <param name="app">The application builder.</param>
        /// <param name="configureContentTypes">Optional content type mappings.</param>
        /// <param name="enableSpaFallback">If true, rewrites deep links under the mount to the mount directory.</param>
        /// <returns>The same <see cref="IApplicationBuilder"/> for chaining.</returns>
        public static IApplicationBuilder UseDynamicNonAssetFiles(
            this IApplicationBuilder app,
            Action<FileExtensionContentTypeProvider>? configureContentTypes = null,
            bool enableSpaFallback = true)
        {
            return app.UseNonAssetFiles(
                mountPath: "/dynamic",
                configureContentTypes: configureContentTypes,
                enableSpaFallback: enableSpaFallback);
        }

        /// <summary>
        /// Serves a static-only folder mounted at <paramref name="mountPath"/> from <c>wwwroot</c> plus the mount segment.
        /// </summary>
        /// <remarks>
        /// If <paramref name="mountPath"/> is null or whitespace, it defaults to <c>/dynamic</c>.
        /// The physical folder is derived from the mount, for example <c>/dynamic</c> maps to <c>wwwroot/dynamic</c>.
        ///
        /// For deep links (no file extension) that look like navigation requests, the path is rewritten to the mount directory
        /// so DefaultFiles can resolve <c>index.html</c>.
        /// </remarks>
        /// <param name="app">The application builder.</param>
        /// <param name="mountPath">Request mount path, for example <c>/dynamic</c>.</param>
        /// <param name="configureContentTypes">Optional content type mappings.</param>
        /// <param name="enableSpaFallback">If true, rewrites deep links under the mount to the mount directory.</param>
        /// <returns>The same <see cref="IApplicationBuilder"/> for chaining.</returns>
        public static IApplicationBuilder UseNonAssetFiles(
            this IApplicationBuilder app,
            string? mountPath = null,
            Action<FileExtensionContentTypeProvider>? configureContentTypes = null,
            bool enableSpaFallback = true)
        {
            ArgumentNullException.ThrowIfNull(app);

            mountPath = string.IsNullOrWhiteSpace(mountPath) ? "/dynamic" : mountPath.Trim();
            if (!mountPath.StartsWith("/", StringComparison.Ordinal))
                mountPath = "/" + mountPath;

            // Normalize: treat "/dynamic/" same as "/dynamic"
            mountPath = mountPath.TrimEnd('/');
            if (mountPath.Length == 0)
                throw new ArgumentException("Mount path must not resolve to empty.", nameof(mountPath));

            // Derive folder under web root from mount path: "/dynamic" -> "dynamic", "/foo/bar" -> "foo/bar"
            var folderUnderWebRoot = mountPath.TrimStart('/');
            if (string.IsNullOrWhiteSpace(folderUnderWebRoot))
                throw new ArgumentException("Mount path must contain at least one segment.", nameof(mountPath));

            var env = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();
            if (string.IsNullOrWhiteSpace(env.WebRootPath))
                throw new InvalidOperationException("WebRootPath is not set. Ensure a web root is configured.");

            var webRootFull = Path.GetFullPath(env.WebRootPath);
            var folderFull = Path.GetFullPath(Path.Combine(webRootFull, folderUnderWebRoot));

            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            // Reviewer note: Prevent traversal escaping the web root.
            if (!folderFull.StartsWith(webRootFull, pathComparison))
                throw new InvalidOperationException("The derived folder resolves outside of the web root.");

            Directory.CreateDirectory(folderFull);

            var requestPath = new PathString(mountPath);
            var fileProvider = new PhysicalFileProvider(folderFull);

            var contentTypes = new FileExtensionContentTypeProvider();
            configureContentTypes?.Invoke(contentTypes);

            app.UseWhen(
                ctx => ctx.Request.Path.StartsWithSegments(requestPath),
                branch =>
                {
                    // If routing already selected an endpoint, StaticFileMiddleware can effectively no-op.
                    // For this static-only subtree, clearing the endpoint is safe.
                    branch.Use(static async (ctx, next) =>
                    {
                        var ep = ctx.GetEndpoint();
                        if (ep?.RequestDelegate is not null)
                        {
                            ctx.SetEndpoint(null);
                            ctx.Request.RouteValues?.Clear();
                        }

                        await next().ConfigureAwait(false);
                    });

                    // Normalize "/dynamic" to "/dynamic/" so DefaultFiles can kick in.
                    branch.Use(async (ctx, next) =>
                    {
                        if (ctx.Request.Path.Equals(requestPath, StringComparison.Ordinal))
                            ctx.Request.Path = requestPath.Add(new PathString("/"));

                        await next().ConfigureAwait(false);
                    });

                    if (enableSpaFallback)
                    {
                        // Rewrite deep links under the mount (no extension) to the mount directory,
                        // so DefaultFiles serves index.html. Keep this conservative to avoid rewriting non-navigation calls.
                        branch.Use(async (ctx, next) =>
                        {
                            if (ctx.Request.Path.StartsWithSegments(requestPath, out var remaining))
                            {
                                var remainingValue = remaining.Value ?? string.Empty;

                                // Only consider paths deeper than "/".
                                if (!string.IsNullOrEmpty(remainingValue) && remainingValue != "/")
                                {
                                    // If there is an extension, it is an asset-like request, do not rewrite.
                                    if (!Path.HasExtension(remainingValue))
                                    {
                                        var rel = remainingValue.TrimStart('/');
                                        var info = fileProvider.GetFileInfo(rel);

                                        // If it is not an existing file, treat it as a SPA route and rewrite to mount directory.
                                        if (!info.Exists)
                                            ctx.Request.Path = requestPath.Add(new PathString("/"));
                                    }
                                }
                            }

                            await next().ConfigureAwait(false);
                        });
                    }

                    // Only serve index.html as the default (deterministic SPA behavior).
                    branch.UseDefaultFiles(new DefaultFilesOptions
                    {
                        RequestPath = requestPath,
                        FileProvider = fileProvider,
                        DefaultFileNames = { "index.html" }
                    });

                    branch.UseStaticFiles(new StaticFileOptions
                    {
                        RequestPath = requestPath,
                        FileProvider = fileProvider,
                        ContentTypeProvider = contentTypes
                    });

                    // Terminal 404 for anything not served by static files in this subtree.
                    branch.Run(static ctx =>
                    {
                        if (!ctx.Response.HasStarted)
                            ctx.Response.StatusCode = StatusCodes.Status404NotFound;

                        return Task.CompletedTask;
                    });
                });

            return app;
        }
    }
}
