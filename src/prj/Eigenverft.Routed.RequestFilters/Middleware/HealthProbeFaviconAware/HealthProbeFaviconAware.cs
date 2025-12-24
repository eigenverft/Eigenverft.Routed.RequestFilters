using System;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Eigenverft.Routed.RequestFilters.Middleware.HealthProbeFaviconAware
{
    /// <summary>
    /// Middleware that responds with 200 OK to requests for a configured health probe path and skips the rest of the pipeline for that path.
    /// </summary>
    /// <remarks>
    /// Browsers may request <c>/favicon.ico</c> after opening the probe path. This middleware answers that favicon request with
    /// 204 No Content only when it was triggered from the probe path (based on the Referer header), reducing debug noise.
    /// </remarks>
    public sealed class HealthProbeFaviconAware
    {
        private const string DefaultContentType = "text/plain; charset=utf-8";
        private static readonly PathString FaviconPath = new("/favicon.ico");

        private readonly RequestDelegate _next;
        private readonly IOptionsMonitor<HealthProbeFaviconAwareOptions> _optionsMonitor;
        private readonly IDeferredLogger<HealthProbeFaviconAware> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="HealthProbeFaviconAware"/> class.
        /// </summary>
        /// <param name="next">The next middleware in the pipeline.</param>
        /// <param name="optionsMonitor">The options monitor for <see cref="HealthProbeFaviconAwareOptions"/>.</param>
        public HealthProbeFaviconAware(RequestDelegate next, IDeferredLogger<HealthProbeFaviconAware> logger, IOptionsMonitor<HealthProbeFaviconAwareOptions> optionsMonitor)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _optionsMonitor.OnChange(_ => _logger.LogDebug("Configuration for {MiddlewareName} updated.", () => nameof(HealthProbeFaviconAware)));
        }

        /// <summary>
        /// Processes the current request.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            HealthProbeFaviconAwareOptions o = _optionsMonitor.CurrentValue ?? new HealthProbeFaviconAwareOptions();

            string probePathText = NormalizePath(o.Path, "/health");
            PathString probePath = new(probePathText);
            string responseBody = string.IsNullOrWhiteSpace(o.ResponseBody) ? "OK" : o.ResponseBody;

            PathString path = context.Request.Path;

            if (path.Equals(probePath, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = DefaultContentType;
                context.Response.Headers.CacheControl = "no-store, no-cache";
                context.Response.Headers.Pragma = "no-cache";

                if (HttpMethods.IsHead(context.Request.Method)) return;

                await context.Response.WriteAsync(responseBody);
                return;
            }

            if (path.Equals(FaviconPath, StringComparison.OrdinalIgnoreCase) &&
                (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) &&
                IsRefererProbe(context.Request.Headers, probePathText))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            await _next(context);
        }

        private static string NormalizePath(string? value, string fallback)
        {
            string s = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            return s.StartsWith("/", StringComparison.Ordinal) ? s : "/" + s;
        }

        private static bool IsRefererProbe(IHeaderDictionary headers, string probePathText)
        {
            if (!headers.TryGetValue("Referer", out StringValues refererValues)) return false;

            string referer = refererValues.ToString();
            if (string.IsNullOrWhiteSpace(referer)) return false;

            // Fast pre-check to avoid URI parsing if the probe path is not present at all.
            if (referer.IndexOf(probePathText, StringComparison.OrdinalIgnoreCase) < 0) return false;

            if (Uri.TryCreate(referer, UriKind.Absolute, out Uri? absoluteUri))
            {
                return absoluteUri.AbsolutePath.Equals(probePathText, StringComparison.OrdinalIgnoreCase);
            }

            if (Uri.TryCreate(referer, UriKind.Relative, out Uri? relativeUri))
            {
                return string.Equals(relativeUri.OriginalString, probePathText, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
    }
}
