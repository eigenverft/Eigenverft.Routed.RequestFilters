using System;
using System.Text;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.GenericExtensions.HttpResponseExtensions;
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
    /// Middleware that requires a simple “browser bootstrap” signal before allowing access to protected index paths.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This middleware performs two separate steps:
    /// </para>
    /// <para>
    /// 1) <b>Bootstrap attempt</b>: If a protected path is requested and the bootstrap cookie is missing/invalid, it returns
    /// a small HTML page that tries to set the cookie via JavaScript and then navigates to an internal outcome endpoint.
    /// </para>
    /// <para>
    /// 2) <b>Outcome decision</b>: The outcome endpoint either continues to the real index request or blocks, depending on
    /// <see cref="BrowserBootstrapFilteringOptions.AllowRequestsWithoutBootstrapCookie"/>.
    /// </para>
    /// <para>
    /// This is a heuristic “browser present” signal and not a security boundary. Clients that execute JavaScript can pass it.
    /// Place this middleware before
    /// <see cref="Microsoft.AspNetCore.Builder.DefaultFilesExtensions.UseDefaultFiles(Microsoft.AspNetCore.Builder.IApplicationBuilder)"/>
    /// and
    /// <see cref="Microsoft.AspNetCore.Builder.StaticFileExtensions.UseStaticFiles(Microsoft.AspNetCore.Builder.IApplicationBuilder)"/>
    /// so it can intercept initial index requests.
    /// </para>
    /// </remarks>
    public sealed class BrowserBootstrapFiltering
    {
        private const string BootstrapCookieValue = "1";

        private const string BootstrapContinuePath = "/_bootstrap/continue";
        private const string BootstrapFailurePath = "/_bootstrap/fail";

        private static readonly PathString ContinueRewritePath = new("/");

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

            _optionsMonitor.OnChange(_ => _logger.LogDebug(
                "Configuration for {MiddlewareName} updated.",
                () => nameof(BrowserBootstrapFiltering)));
        }

        /// <summary>
        /// Processes the current request and either forwards it, returns the bootstrap attempt HTML,
        /// or handles one of the bootstrap outcome endpoints.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            BrowserBootstrapFilteringOptions options = _optionsMonitor.CurrentValue;

            // Only meaningful for “document-like” navigations.
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                await _next(context);
                return;
            }

            PathString path = context.Request.Path;

            // Outcome endpoints (internal).
            if (IsPath(path, BootstrapContinuePath, options.CaseSensitivePaths))
            {
                await HandleBootstrapContinueAsync(context, options);
                return;
            }

            if (IsPath(path, BootstrapFailurePath, options.CaseSensitivePaths))
            {
                await HandleBootstrapFailureAsync(context, options);
                return;
            }

            // Only protect selected index-ish paths.
            if (!IsProtectedPath(path, options))
            {
                await _next(context);
                return;
            }

            string trace = context.TraceIdentifier;
            string observed = path.HasValue ? path.Value! : string.Empty;

            // Already bootstrapped => allow straight through (real index.html comes from StaticFiles/DefaultFiles).
            if (HasValidBootstrapCookie(context, options))
            {
                FilterDecisionLogEntry okLog = FilterDecisionLogBuilder.Create(
                    nameof(BrowserBootstrapFiltering),
                    trace,
                    FilterMatchKind.Whitelist,
                    isAllowed: true,
                    observedValue: observed,
                    loggedForEvaluator: false,
                    logLevelWhitelist: options.LogLevelAllowedRequests,
                    logLevelBlacklist: options.LogLevelAllowedRequests,
                    logLevelUnmatched: options.LogLevelAllowedRequests);

                if (okLog.Level != LogLevel.None && _logger.IsEnabled(okLog.Level))
                {
                    _logger.Log(okLog.Level, okLog.MessageTemplate, okLog.Args);
                }

                await _next(context);
                return;
            }

            // Missing/invalid cookie => always attempt bootstrap (distinct from the outcome decision).
            // This is NOT a classifier result; log it with a local template and a dedicated option.
            LogBootstrapAttempt(trace, observed, options);

            await WriteBootstrapAttemptHtmlAsync(context, options);
        }

        private async Task HandleBootstrapContinueAsync(HttpContext context, BrowserBootstrapFilteringOptions options)
        {
            string trace = context.TraceIdentifier;
            string observed = context.Request.Path.HasValue ? context.Request.Path.Value! : string.Empty;

            bool hasCookie = HasValidBootstrapCookie(context, options);

            // Continue endpoint means: “bootstrap JS ran and we believe we set/read cookie”.
            // Still validate server-side.
            if (hasCookie)
            {
                FilterDecisionLogEntry okLog = FilterDecisionLogBuilder.Create(
                    nameof(BrowserBootstrapFiltering),
                    trace,
                    FilterMatchKind.Whitelist,
                    isAllowed: true,
                    observedValue: observed,
                    loggedForEvaluator: false,
                    logLevelWhitelist: options.LogLevelAllowedRequests,
                    logLevelBlacklist: options.LogLevelAllowedRequests,
                    logLevelUnmatched: options.LogLevelAllowedRequests);

                if (okLog.Level != LogLevel.None && _logger.IsEnabled(okLog.Level))
                {
                    _logger.Log(okLog.Level, okLog.MessageTemplate, okLog.Args);
                }

                context.Request.Path = ContinueRewritePath;
                await _next(context);
                return;
            }

            // Cookie still missing at “continue” => treat as bootstrap failure.
            await HandleBootstrapFailureAsync(context, options);
        }

        private async Task HandleBootstrapFailureAsync(HttpContext context, BrowserBootstrapFilteringOptions options)
        {
            string trace = context.TraceIdentifier;
            string observed = context.Request.Path.HasValue ? context.Request.Path.Value! : string.Empty;

            if (options.RecordRequestsWithoutBootstrapCookie)
            {
                await _filteringEventStorage.StoreAsync(new FilteringEvent
                {
                    TimestampUtc = DateTime.UtcNow,
                    EventSource = nameof(BrowserBootstrapFiltering),
                    MatchKind = FilterMatchKind.Unmatched,
                    RemoteIpAddress = context.GetRemoteIpAddress(),
                    ObservedValue = observed
                });
            }

            bool isAllowed = options.AllowRequestsWithoutBootstrapCookie;

            FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                nameof(BrowserBootstrapFiltering),
                trace,
                FilterMatchKind.Unmatched,
                isAllowed,
                observedValue: observed,
                loggedForEvaluator: options.RecordRequestsWithoutBootstrapCookie,
                logLevelWhitelist: options.LogLevelBootstrapOutcome,
                logLevelBlacklist: options.LogLevelBootstrapOutcome,
                logLevelUnmatched: options.LogLevelBootstrapOutcome);

            if (log.Level != LogLevel.None && _logger.IsEnabled(log.Level))
            {
                _logger.Log(log.Level, log.MessageTemplate, log.Args);
            }

            if (isAllowed)
            {
                // Rollout / accessibility mode: allow even though cookie couldn't be established.
                context.Request.Path = ContinueRewritePath;
                await _next(context);
                return;
            }

            // Enforcement mode: short-circuit with a standard block response.
            await context.Response.WriteDefaultStatusCodeAnswerEx(options.BlockStatusCode);
        }

        /// <summary>
        /// Logs that a bootstrap attempt HTML response is being served.
        /// </summary>
        /// <remarks>
        /// This is intentionally not routed through <see cref="FilterDecisionLogBuilder"/> because it is not a real
        /// whitelist/blacklist/unmatched classifier result; it is the bootstrap attempt step.
        /// </remarks>
        /// <param name="traceIdentifier">The current trace identifier.</param>
        /// <param name="observedValue">Observed request path.</param>
        /// <param name="options">The current options snapshot.</param>
        private void LogBootstrapAttempt(string traceIdentifier, string observedValue, BrowserBootstrapFilteringOptions options)
        {
            LogLevel level = options.LogLevelBootstrapAttempt;
            if (level == LogLevel.None || !_logger.IsEnabled(level))
            {
                return;
            }

            _logger.Log(
                level,
                "{Middleware} action={Action} observed={Observed} trace={Trace}",
                () => nameof(BrowserBootstrapFiltering),
                () => "BootstrapAttempt",
                () => observedValue,
                () => traceIdentifier);
        }

        private static bool IsProtectedPath(PathString requestPath, BrowserBootstrapFilteringOptions options)
        {
            if (!requestPath.HasValue)
            {
                return false;
            }

            var list = options.ProtectedPaths;
            if (list == null || list.Length == 0)
            {
                return false;
            }

            string path = requestPath.Value!;
            var comparison = options.CaseSensitivePaths ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            for (int i = 0; i < list.Length; i++)
            {
                string candidate = list[i] ?? string.Empty;
                if (candidate.Length == 0) continue;

                if (string.Equals(path, candidate, comparison))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPath(PathString requestPath, string expected, bool caseSensitive)
        {
            if (!requestPath.HasValue)
            {
                return false;
            }

            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return string.Equals(requestPath.Value!, expected, comparison);
        }

        private static bool HasValidBootstrapCookie(HttpContext context, BrowserBootstrapFilteringOptions options)
        {
            if (context.Request.Cookies.TryGetValue(options.CookieName, out string? value))
            {
                return string.Equals(value, BootstrapCookieValue, StringComparison.Ordinal);
            }

            return false;
        }

        private static Task WriteBootstrapAttemptHtmlAsync(HttpContext context, BrowserBootstrapFilteringOptions options)
        {
            // Must be 200 so the browser executes JS. This is not the enforcement decision.
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/html; charset=utf-8";

            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";

            string html = BuildBootstrapAttemptHtml(options, context.TraceIdentifier);
            return context.Response.WriteAsync(html);
        }

        private static string BuildBootstrapAttemptHtml(BrowserBootstrapFilteringOptions options, string traceIdentifier)
        {
            static string JsEscape(string s) => (s ?? string.Empty)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("'", "\\'", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);

            string cookieName = JsEscape(options.CookieName);
            string cookieValue = JsEscape(BootstrapCookieValue);
            string continuePath = JsEscape(BootstrapContinuePath);
            string failPath = JsEscape(BootstrapFailurePath);

            int maxAgeSeconds = (int)Math.Clamp(options.CookieMaxAge.TotalSeconds, 60, int.MaxValue);
            string trace = JsEscape(traceIdentifier);

            var sb = new StringBuilder(1024);

            sb.AppendLine("<!doctype html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("  <meta charset=\"utf-8\" />");
            sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
            sb.AppendLine("  <meta name=\"robots\" content=\"noindex, nofollow\" />");
            sb.AppendLine("  <title>Loading…</title>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("  <noscript>");
            sb.AppendLine("    <p>JavaScript is required to continue. If the problem persists, please contact customer support and provide this Trace Id:</p>");
            sb.Append("    <pre>").Append(trace).AppendLine("</pre>");
            sb.AppendLine("  </noscript>");
            sb.AppendLine("  <script>");
            sb.AppendLine("  (function(){");
            sb.AppendLine("    function hasCookie(name, value){");
            sb.AppendLine("      var needle = name + '=' + value;");
            sb.AppendLine("      return document.cookie && document.cookie.indexOf(needle) >= 0;");
            sb.AppendLine("    }");
            sb.AppendLine("    try {");
            sb.AppendLine("      var secure = (location.protocol === 'https:') ? '; Secure' : '';");
            sb.AppendLine("      document.cookie = '" + cookieName + "=" + cookieValue + "; Path=/; Max-Age=" + maxAgeSeconds + "; SameSite=Lax' + secure;");
            sb.AppendLine("    } catch (e) { }");
            sb.AppendLine("    if (hasCookie('" + cookieName + "','" + cookieValue + "')) {");
            sb.AppendLine("      location.replace('" + continuePath + "');");
            sb.AppendLine("    } else {");
            sb.AppendLine("      location.replace('" + failPath + "');");
            sb.AppendLine("    }");
            sb.AppendLine("  })();");
            sb.AppendLine("  </script>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }
    }
}
