using System;
using System.Globalization;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.BrowserBootstrapFiltering
{
    /// <summary>
    /// Middleware that protects entry pages by issuing a lightweight browser bootstrap challenge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This middleware is intended to run before static files. When the bootstrap cookie is present it passes through so the
    /// real <c>index.html</c> can be served by the static file middleware.
    /// </para>
    /// <para>
    /// When the cookie is missing on an entry path, it responds with a small HTML page that sets the cookie via JavaScript and reloads
    /// the same URL with a bootstrap marker query parameter.
    /// </para>
    /// <para>
    /// If the bootstrap marker is present but the cookie is still missing, the request is treated as suspicious and may be blocked.
    /// </para>
    /// </remarks>
    public sealed class BrowserBootstrapFiltering
    {
        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<BrowserBootstrapFiltering> _logger;
        private readonly IOptionsMonitor<BrowserBootstrapFilteringOptions> _optionsMonitor;
        private readonly IFilteringEventStorage _filteringEventStorage;

        /// <summary>
        /// Initializes a new instance of the <see cref="BrowserBootstrapFiltering"/> class.
        /// </summary>
        /// <param name="nextMiddleware">The next middleware in the pipeline.</param>
        /// <param name="logger">The deferred logger instance.</param>
        /// <param name="optionsMonitor">The options monitor for <see cref="BrowserBootstrapFilteringOptions"/>.</param>
        /// <param name="filteringEventStorage">The central filtering event storage.</param>
        public BrowserBootstrapFiltering(
            RequestDelegate nextMiddleware,
            IDeferredLogger<BrowserBootstrapFiltering> logger,
            IOptionsMonitor<BrowserBootstrapFilteringOptions> optionsMonitor,
            IFilteringEventStorage filteringEventStorage)
        {
            _next = nextMiddleware ?? throw new ArgumentNullException(nameof(nextMiddleware));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _filteringEventStorage = filteringEventStorage ?? throw new ArgumentNullException(nameof(filteringEventStorage));

            _optionsMonitor.OnChange(_ =>
                _logger.LogDebug("Configuration for {MiddlewareName} updated.", () => nameof(BrowserBootstrapFiltering)));
        }

        /// <summary>
        /// Processes the current request and either forwards it, issues a bootstrap challenge, or blocks it.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context is null) throw new ArgumentNullException(nameof(context));

            // Only meaningful for browser entry requests.
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                await _next(context);
                return;
            }

            BrowserBootstrapFilteringOptions options = _optionsMonitor.CurrentValue;

            if (!IsEntryPath(context, options))
            {
                await _next(context);
                return;
            }

            string trace = context.TraceIdentifier;
            string observed = context.Request.Path + context.Request.QueryString;

            bool hasCookie = TryGetBootstrapCookie(context, options, out bool cookieMatches) && cookieMatches;
            bool hasBootstrapMarker = context.Request.Query.ContainsKey(options.BootstrapQueryParameterName);

            // Whitelist: cookie is present and matches.
            if (hasCookie)
            {
                LogDecision(options.LogLevelWhitelist, trace, FilterMatchKind.Whitelist, "Allowed", observed, loggedForEvaluator: false, reason: "bootstrap-cookie-present");
                await _next(context);
                return;
            }

            // Blacklist: the request came back with the marker but still has no cookie (cookies blocked, no JS, etc.).
            if (hasBootstrapMarker)
            {
                if (options.RecordBlacklistedRequests)
                {
                    await _filteringEventStorage.StoreAsync(new FilteringEvent
                    {
                        TimestampUtc = DateTime.UtcNow,
                        EventSource = nameof(BrowserBootstrapFiltering),
                        MatchKind = FilterMatchKind.Blacklist,
                        RemoteIpAddress = context.GetRemoteIpAddress(),
                        ObservedValue = observed,
                    });
                }

                bool isAllowed = options.AllowBlacklistedRequests;

                LogDecision(
                    options.LogLevelBlacklist,
                    trace,
                    FilterMatchKind.Blacklist,
                    isAllowed ? "Allowed" : "Blocked",
                    observed,
                    loggedForEvaluator: options.RecordBlacklistedRequests,
                    reason: "bootstrap-failed-cookie-missing");

                if (isAllowed)
                {
                    await _next(context);
                    return;
                }

                await WriteSupportResponseAsync(context, options.BlockStatusCode);
                return;
            }

            // Unmatched: first entry without cookie; issue bootstrap HTML (challenge) and stop pipeline here.
            if (options.RecordUnmatchedRequests)
            {
                await _filteringEventStorage.StoreAsync(new FilteringEvent
                {
                    TimestampUtc = DateTime.UtcNow,
                    EventSource = nameof(BrowserBootstrapFiltering),
                    MatchKind = FilterMatchKind.Unmatched,
                    RemoteIpAddress = context.GetRemoteIpAddress(),
                    ObservedValue = observed,
                });
            }

            LogDecision(
                options.LogLevelUnmatched,
                trace,
                FilterMatchKind.Unmatched,
                "Bootstrap",
                observed,
                loggedForEvaluator: options.RecordUnmatchedRequests,
                reason: "bootstrap-issued-cookie-missing");

            await WriteBootstrapHtmlAsync(context, options);
        }

        private static bool IsEntryPath(HttpContext context, BrowserBootstrapFilteringOptions options)
        {
            string path = context.Request.Path.Value ?? string.Empty;

            var entryPaths = options.EntryPaths;
            if (entryPaths is null || entryPaths.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < entryPaths.Count; i++)
            {
                string candidate = entryPaths[i] ?? string.Empty;
                if (candidate.Length == 0) continue;

                if (string.Equals(path, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetBootstrapCookie(HttpContext context, BrowserBootstrapFilteringOptions options, out bool matches)
        {
            matches = false;

            if (context.Request.Cookies is null)
            {
                return false;
            }

            if (!context.Request.Cookies.TryGetValue(options.CookieName, out string? value))
            {
                return false;
            }

            matches = string.Equals(value, options.CookieValue, StringComparison.Ordinal);
            return true;
        }

        private void LogDecision(
            LogLevel level,
            string traceIdentifier,
            FilterMatchKind matchKind,
            string decision,
            string observedValue,
            bool loggedForEvaluator,
            string reason)
        {
            if (level == LogLevel.None || !_logger.IsEnabled(level)) return;

            _logger.Log(
                level,
                "{Middleware} match={Match} decision={Decision} observed={Observed} loggedForEvaluator={Logged} reason={Reason} trace={Trace}",
                () => nameof(BrowserBootstrapFiltering),
                () => matchKind,
                () => decision,
                () => observedValue,
                () => loggedForEvaluator,
                () => reason,
                () => traceIdentifier);
        }

        private static async Task WriteBootstrapHtmlAsync(HttpContext context, BrowserBootstrapFilteringOptions options)
        {
            // Intentionally keep this tiny. It is not a “real page”, just a bootstrap step.
            // The reload includes a marker so the server can detect “cookie still missing” and stop loops.
            string cookie = BuildCookieString(context, options);
            string marker = Uri.EscapeDataString(options.BootstrapQueryParameterName);

            string html =
$@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""robots"" content=""noindex,nofollow"">
  <meta http-equiv=""cache-control"" content=""no-store"">
  <meta http-equiv=""pragma"" content=""no-cache"">
  <meta http-equiv=""expires"" content=""0"">
  <title>Loading…</title>
</head>
<body>
<script>
(function() {{
  try {{
    document.cookie = {ToJsStringLiteral(cookie)};
    var u = new URL(window.location.href);
    if (!u.searchParams.has({ToJsStringLiteral(marker)})) {{
      u.searchParams.set({ToJsStringLiteral(marker)}, ""1"");
    }}
    window.location.replace(u.toString());
  }} catch (e) {{
    // If JS fails, the next request will still be missing the cookie and the server will handle it.
    window.location.reload();
  }}
}})();
</script>
<noscript>This service requires a browser with JavaScript enabled.</noscript>
</body>
</html>";

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store, no-cache";
            context.Response.Headers.Pragma = "no-cache";

            await context.Response.WriteAsync(html);
        }

        private static string BuildCookieString(HttpContext context, BrowserBootstrapFilteringOptions options)
        {
            // Keep it compatible: simple name/value + Max-Age + Path + SameSite=Lax (+ Secure when HTTPS).
            long maxAgeSeconds = (long)Math.Max(0, options.CookieMaxAge.TotalSeconds);

            bool isHttps = context.Request.IsHttps;
            string secure = isHttps ? "; Secure" : string.Empty;

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{options.CookieName}={options.CookieValue}; Max-Age={maxAgeSeconds}; Path=/; SameSite=Lax{secure}");
        }

        private static string ToJsStringLiteral(string value)
        {
            // Minimal JS escaping for embedding in a single-quoted literal.
            // This is not a general-purpose encoder, but is sufficient for controlled cookie strings here.
            if (value is null) return "''";
            string escaped = value.Replace("\\", "\\\\").Replace("'", "\\'");
            return "'" + escaped + "'";
        }

        private static async Task WriteSupportResponseAsync(HttpContext context, int statusCode)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "text/plain; charset=utf-8";

            string trace = context.TraceIdentifier ?? string.Empty;

            await context.Response.WriteAsync(
                "Your request could not be processed. Please contact customer support and provide this Trace ID: " + trace + ".");
        }
    }
}
