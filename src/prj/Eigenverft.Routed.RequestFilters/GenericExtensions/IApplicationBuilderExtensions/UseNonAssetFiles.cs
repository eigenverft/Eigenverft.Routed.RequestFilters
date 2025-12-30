using System;
using System.IO;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Eigenverft.App.GlobalServerPwaHost
{
    /// <summary>
    /// Provides middleware helpers to serve an isolated “static-only” subtree from <c>wwwroot</c>
    /// before endpoint fallbacks (for example <c>MapStaticAssets()</c> and <c>MapRazorComponents(...)</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The intent is to host a client-side app (PWA / SPA) under a mount path (for example <c>/app</c>)
    /// while preventing later endpoints (Razor Components, Identity endpoints, fallback endpoints) from handling that subtree.
    /// </para>
    /// <para>
    /// This is achieved by branching with <see cref="IApplicationBuilder.UseWhen(Func{HttpContext, bool}, Action{IApplicationBuilder})"/>
    /// and making the branch terminal: if no file is served, the branch returns <c>404</c>.
    /// </para>
    /// <para>
    /// A critical detail: if endpoint routing has already selected an endpoint for the request, static file middleware can no-op.
    /// Therefore, inside the static-only branch we clear a selected endpoint (when it has a delegate) so the static pipeline
    /// can serve files reliably and the request cannot “fall through” into later endpoint handlers.
    /// </para>
    /// </remarks>
    public static class NonAssetFilesExtensions
    {
        /// <summary>
        /// Serves a static-only folder mounted at <c>/dynamic</c> from <c>wwwroot/dynamic</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is a convenience wrapper over <see cref="UseNonAssetFiles(IApplicationBuilder, string?, Action{FileExtensionContentTypeProvider}?)"/>.
        /// </para>
        /// <para>
        /// The branch is terminal: if a file is not served, the response is <c>404</c> and no later middleware/endpoints run.
        /// Default files are enabled, so <c>/dynamic/</c> can serve <c>index.html</c>.
        /// </para>
        /// </remarks>
        /// <param name="app">The application builder.</param>
        /// <param name="configureContentTypes">Optional content type mappings (for example PWA / Blazor extensions).</param>
        /// <returns>The same <see cref="IApplicationBuilder"/> instance for chaining.</returns>
        /// <example>
        /// <code>
        /// app.UseDynamicNonAssetFiles(p =&gt; p.AddPwaAndBlazorMappings());
        /// </code>
        /// </example>
        public static IApplicationBuilder UseDynamicNonAssetFiles(
            this IApplicationBuilder app,
            Action<FileExtensionContentTypeProvider>? configureContentTypes = null)
        {
            return app.UseNonAssetFiles(
                mountPath: "/dynamic",
                configureContentTypes: configureContentTypes);
        }

        /// <summary>
        /// Serves a static-only folder mounted at <paramref name="mountPath"/> from <c>wwwroot</c> plus the mount segment.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If <paramref name="mountPath"/> is null or whitespace, it defaults to <c>/dynamic</c>.
        /// The physical folder is derived from the mount path:
        /// <c>/dynamic</c> maps to <c>wwwroot/dynamic</c>, and <c>/foo/bar</c> maps to <c>wwwroot/foo/bar</c>.
        /// </para>
        /// <para>
        /// Default file behavior: the branch enables <see cref="DefaultFilesMiddleware"/> and serves only <c>index.html</c>
        /// as the default file name. This provides deterministic “app root” behavior for folder URLs.
        /// </para>
        /// <para>
        /// Pipeline ordering note: this should be placed before endpoint mappings such as <c>MapStaticAssets()</c>,
        /// <c>MapRazorComponents(...)</c>, and any fallbacks, so the static subtree is served first and remains isolated.
        /// </para>
        /// </remarks>
        /// <param name="app">The application builder.</param>
        /// <param name="mountPath">Request mount path, for example <c>/app</c> or <c>app</c>.</param>
        /// <param name="configureContentTypes">Optional content type mappings.</param>
        /// <returns>The same <see cref="IApplicationBuilder"/> instance for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="mountPath"/> normalizes to an empty value.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when <see cref="IWebHostEnvironment.WebRootPath"/> is not set, or when the derived physical folder escapes the web root.
        /// </exception>
        /// <example>
        /// <code>
        /// // In Program.Main, before MapStaticAssets() and MapRazorComponents(...)
        /// app.UseNonAssetFiles("apps", p =&gt; p.AddPwaAndBlazorMappings());
        /// </code>
        /// </example>
        public static IApplicationBuilder UseNonAssetFiles(
            this IApplicationBuilder app,
            string? mountPath = null,
            Action<FileExtensionContentTypeProvider>? configureContentTypes = null)
        {
            ArgumentNullException.ThrowIfNull(app);

            // Reviewer note: Accept "/app" and "app", normalize to "/app".
            mountPath = string.IsNullOrWhiteSpace(mountPath) ? "/dynamic" : mountPath.Trim();
            if (!mountPath.StartsWith("/", StringComparison.Ordinal))
                mountPath = "/" + mountPath;

            // Reviewer note: Normalize "/app/" to "/app".
            mountPath = mountPath.TrimEnd('/');
            if (mountPath.Length == 0)
                throw new ArgumentException("Mount path must not resolve to empty.", nameof(mountPath));

            // Reviewer note: Derive folder under web root from mount path: "/app" -> "app", "/foo/bar" -> "foo/bar".
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
                    // Reviewer note:
                    // If endpoint routing already selected an endpoint (and it has a delegate),
                    // static file middleware may not serve. Clearing it in this static-only subtree
                    // ensures the file pipeline can run and avoids falling through into later endpoints.
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

                    // Reviewer note: Normalize "/app" to "/app/" so DefaultFiles can match.
                    branch.Use(async (ctx, next) =>
                    {
                        if (ctx.Request.Path.Equals(requestPath, StringComparison.Ordinal))
                            ctx.Request.Path = requestPath.Add(new PathString("/"));

                        await next().ConfigureAwait(false);
                    });

                    // Reviewer note: Only serve index.html as the default (deterministic SPA behavior).
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

                    // Reviewer note: Terminal 404 for anything not served by static files in this subtree.
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
