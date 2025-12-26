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
using Microsoft.Extensions.Primitives;

namespace Eigenverft.Routed.RequestFilters.Middleware.BrowserBootstrapFiltering
{
    /// <summary>
    /// Middleware that requires a simple “browser bootstrap” signal (cookie) before allowing access to in-scope paths.
    /// </summary>
    /// <remarks>
    /// Reviewer note: This implementation preserves the originally requested target via a <c>to</c> query parameter on
    /// internal outcome endpoints. A single-shot loop breaker is implemented using a private query flag appended only on
    /// the failure outcome redirect, so clients that cannot set cookies do not get stuck in infinite bootstrap loops.
    /// </remarks>
    public sealed class BrowserBootstrapFiltering
    {
        private const string BootstrapCookieValue = "1";

        private const string BootstrapContinuePath = "/_bootstrap/continue";
        private const string BootstrapFailurePath = "/_bootstrap/fail";

        private const string OutcomeTargetQueryKey = "to";

        // One-shot loop breaker flag (added only on the "fail" redirect target).
        private const string BootstrapAttemptFlagKey = "__evf_bootstrap_attempt";
        private const string BootstrapAttemptFlagValue = "1";

        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<BrowserBootstrapFiltering> _logger;
        private readonly IOptionsMonitor<BrowserBootstrapFilteringOptions> _optionsMonitor;
        private readonly IFilteringEventStorage _filteringEventStorage;

        /// <summary>
        /// Initializes a new instance of the <see cref="BrowserBootstrapFiltering"/> class.
        /// </summary>
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

            // Outcome endpoints (internal) are handled first.
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

            string trace = context.TraceIdentifier;
            string observedPath = path.HasValue ? path.Value! : string.Empty;

            // ----------------------------------------------------------------
            // Stage 1: Classify the path (wildcards + exact entries supported)
            // ----------------------------------------------------------------
            FilterMatchKind matchKind = ClassifyPath(observedPath, options);

            if (matchKind == FilterMatchKind.Blacklist)
            {
                await HandleBootstrapExceptionPathAsync(context, options, trace, observedPath);
                return;
            }

            if (matchKind == FilterMatchKind.Unmatched)
            {
                await HandleUnmatchedPathAsync(context, options, trace, observedPath);
                return;
            }

            if (matchKind != FilterMatchKind.Whitelist)
            {
                _logger.LogCritical(
                    "ATTENTION: {MiddlewareName} received an unexpected {EnumType} value '{EnumValue}'. This should not happen.",
                    () => nameof(BrowserBootstrapFiltering),
                    () => nameof(FilterMatchKind),
                    () => matchKind);

                await _next(context);
                return;
            }

            // Optional: classification-only log for “scope”.
            LogScopeClassificationIfEnabled(options, trace, observedPath);

            // ----------------------------------------------------------------
            // Stage 2: Scope => require cookie or serve bootstrap attempt HTML
            // ----------------------------------------------------------------

            // Cookie present => allow straight through.
            if (HasValidBootstrapCookie(context, options))
            {
                // Clean up the one-shot attempt flag if it is present, so downstream does not see it.
                if (HasBootstrapAttemptFlag(context))
                {
                    RemoveBootstrapAttemptFlagFromQuery(context);
                }

                FilterDecisionLogEntry okLog = FilterDecisionLogBuilder.Create(
                    nameof(BrowserBootstrapFiltering),
                    trace,
                    FilterMatchKind.Whitelist,
                    isAllowed: true,
                    observedValue: observedPath,
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

            // One-shot loop breaker:
            // If we already attempted bootstrap once for this target and still have no cookie, do not serve the HTML again.
            if (HasBootstrapAttemptFlag(context))
            {
                await HandleScopeBootstrapFailureAfterAttemptAsync(context, options, trace);
                return;
            }

            // Cookie missing/invalid => serve bootstrap attempt HTML (not the enforcement decision).
            LogBootstrapAttempt(trace, observedPath, options);

            string originalTarget = BuildOriginalTargetOrThrow(context);
            await WriteBootstrapAttemptHtmlAsync(context, options, originalTarget);
        }

        private async Task HandleBootstrapExceptionPathAsync(HttpContext context, BrowserBootstrapFilteringOptions options, string trace, string observedPath)
        {
            if (options.RecordRequestsOnBootstrapExceptionPaths)
            {
                await _filteringEventStorage.StoreAsync(new FilteringEvent
                {
                    TimestampUtc = DateTime.UtcNow,
                    EventSource = nameof(BrowserBootstrapFiltering),
                    MatchKind = FilterMatchKind.Blacklist,
                    RemoteIpAddress = context.GetRemoteIpAddress(),
                    ObservedValue = observedPath
                });
            }

            bool isAllowed = options.AllowRequestsOnBootstrapExceptionPaths;

            FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                nameof(BrowserBootstrapFiltering),
                trace,
                FilterMatchKind.Blacklist,
                isAllowed,
                observedValue: observedPath,
                loggedForEvaluator: options.RecordRequestsOnBootstrapExceptionPaths,
                logLevelWhitelist: options.LogLevelBootstrapScopePaths,
                logLevelBlacklist: options.LogLevelBootstrapExceptionPaths,
                logLevelUnmatched: options.LogLevelUnmatchedPaths);

            if (log.Level != LogLevel.None && _logger.IsEnabled(log.Level))
            {
                _logger.Log(log.Level, log.MessageTemplate, log.Args);
            }

            if (isAllowed)
            {
                await _next(context);
                return;
            }

            await context.Response.WriteDefaultStatusCodeAnswerEx(options.BlockStatusCode);
        }

        private async Task HandleUnmatchedPathAsync(HttpContext context, BrowserBootstrapFilteringOptions options, string trace, string observedPath)
        {
            if (options.RecordRequestsOnUnmatchedPaths)
            {
                await _filteringEventStorage.StoreAsync(new FilteringEvent
                {
                    TimestampUtc = DateTime.UtcNow,
                    EventSource = nameof(BrowserBootstrapFiltering),
                    MatchKind = FilterMatchKind.Unmatched,
                    RemoteIpAddress = context.GetRemoteIpAddress(),
                    ObservedValue = observedPath
                });
            }

            bool isAllowed = options.AllowRequestsOnUnmatchedPaths;

            FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                nameof(BrowserBootstrapFiltering),
                trace,
                FilterMatchKind.Unmatched,
                isAllowed,
                observedValue: observedPath,
                loggedForEvaluator: options.RecordRequestsOnUnmatchedPaths,
                logLevelWhitelist: options.LogLevelBootstrapScopePaths,
                logLevelBlacklist: options.LogLevelBootstrapExceptionPaths,
                logLevelUnmatched: options.LogLevelUnmatchedPaths);

            if (log.Level != LogLevel.None && _logger.IsEnabled(log.Level))
            {
                _logger.Log(log.Level, log.MessageTemplate, log.Args);
            }

            if (isAllowed)
            {
                await _next(context);
                return;
            }

            await context.Response.WriteDefaultStatusCodeAnswerEx(options.BlockStatusCode);
        }

        private async Task HandleBootstrapContinueAsync(HttpContext context, BrowserBootstrapFilteringOptions options)
        {
            string trace = context.TraceIdentifier;

            if (!TryGetSafeOutcomeTarget(context, out string target))
            {
                await context.Response.WriteDefaultStatusCodeAnswerEx(options.BlockStatusCode);
                return;
            }

            if (HasValidBootstrapCookie(context, options))
            {
                FilterDecisionLogEntry okLog = FilterDecisionLogBuilder.Create(
                    nameof(BrowserBootstrapFiltering),
                    trace,
                    FilterMatchKind.Whitelist,
                    isAllowed: true,
                    observedValue: target,
                    loggedForEvaluator: false,
                    logLevelWhitelist: options.LogLevelAllowedRequests,
                    logLevelBlacklist: options.LogLevelAllowedRequests,
                    logLevelUnmatched: options.LogLevelAllowedRequests);

                if (okLog.Level != LogLevel.None && _logger.IsEnabled(okLog.Level))
                {
                    _logger.Log(okLog.Level, okLog.MessageTemplate, okLog.Args);
                }

                context.Response.Redirect(target, permanent: false);
                return;
            }

            // Cookie still missing at “continue” => treat as bootstrap failure.
            await HandleBootstrapFailureAsync(context, options);
        }

        private async Task HandleBootstrapFailureAsync(HttpContext context, BrowserBootstrapFilteringOptions options)
        {
            string trace = context.TraceIdentifier;

            if (!TryGetSafeOutcomeTarget(context, out string target))
            {
                await context.Response.WriteDefaultStatusCodeAnswerEx(options.BlockStatusCode);
                return;
            }

            if (options.RecordRequiredPathsOnBootstrapFailure)
            {
                await _filteringEventStorage.StoreAsync(new FilteringEvent
                {
                    TimestampUtc = DateTime.UtcNow,
                    EventSource = nameof(BrowserBootstrapFiltering),
                    MatchKind = FilterMatchKind.Unmatched,
                    RemoteIpAddress = context.GetRemoteIpAddress(),
                    ObservedValue = target
                });
            }

            bool isAllowed = options.AllowRequiredPathsOnBootstrapFailure;

            FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                nameof(BrowserBootstrapFiltering),
                trace,
                FilterMatchKind.Unmatched,
                isAllowed,
                observedValue: target,
                loggedForEvaluator: options.RecordRequiredPathsOnBootstrapFailure,
                logLevelWhitelist: options.LogLevelBootstrapOutcome,
                logLevelBlacklist: options.LogLevelBootstrapOutcome,
                logLevelUnmatched: options.LogLevelBootstrapOutcome);

            if (log.Level != LogLevel.None && _logger.IsEnabled(log.Level))
            {
                _logger.Log(log.Level, log.MessageTemplate, log.Args);
            }

            if (isAllowed)
            {
                // Note: the bootstrap HTML appends the one-shot attempt flag to the failure target,
                // which breaks infinite loops by making the next in-scope request skip bootstrap HTML.
                context.Response.Redirect(target, permanent: false);
                return;
            }

            await context.Response.WriteDefaultStatusCodeAnswerEx(options.BlockStatusCode);
        }

        /// <summary>
        /// Handles a scope request that already carries the one-shot attempt flag but still has no cookie.
        /// </summary>
        /// <remarks>
        /// Reviewer note: This is the loop breaker. It treats the request as a bootstrap failure outcome
        /// and applies <see cref="BrowserBootstrapFilteringOptions.AllowRequiredPathsOnBootstrapFailure"/>.
        /// </remarks>
        private async Task HandleScopeBootstrapFailureAfterAttemptAsync(HttpContext context, BrowserBootstrapFilteringOptions options, string trace)
        {
            // Build the current target for logging (then strip the internal flag so downstream does not see it).
            string targetWithFlag = BuildOriginalTargetOrThrow(context);

            RemoveBootstrapAttemptFlagFromQuery(context);

            string target = BuildOriginalTargetOrThrow(context);

            if (options.RecordRequiredPathsOnBootstrapFailure)
            {
                await _filteringEventStorage.StoreAsync(new FilteringEvent
                {
                    TimestampUtc = DateTime.UtcNow,
                    EventSource = nameof(BrowserBootstrapFiltering),
                    MatchKind = FilterMatchKind.Unmatched,
                    RemoteIpAddress = context.GetRemoteIpAddress(),
                    ObservedValue = target
                });
            }

            bool isAllowed = options.AllowRequiredPathsOnBootstrapFailure;

            FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                nameof(BrowserBootstrapFiltering),
                trace,
                FilterMatchKind.Unmatched,
                isAllowed,
                observedValue: target,
                loggedForEvaluator: options.RecordRequiredPathsOnBootstrapFailure,
                logLevelWhitelist: options.LogLevelBootstrapOutcome,
                logLevelBlacklist: options.LogLevelBootstrapOutcome,
                logLevelUnmatched: options.LogLevelBootstrapOutcome);

            if (log.Level != LogLevel.None && _logger.IsEnabled(log.Level))
            {
                _logger.Log(log.Level, log.MessageTemplate, log.Args);
            }

            if (isAllowed)
            {
                await _next(context);
                return;
            }

            await context.Response.WriteDefaultStatusCodeAnswerEx(options.BlockStatusCode);
        }

        private static FilterMatchKind ClassifyPath(string observedPath, BrowserBootstrapFilteringOptions options)
        {
            if (string.IsNullOrEmpty(observedPath))
            {
                return FilterMatchKind.Unmatched;
            }

            FilterPriority priority = options.BootstrapConflictPreference == BootstrapConflictPreference.PreferScope
                ? FilterPriority.Whitelist
                : FilterPriority.Blacklist;

            return FilterClassifier.Classify(
                observedPath,
                options.BootstrapScopePathPatterns,
                options.BootstrapExceptionPathPatterns,
                options.CaseSensitivePaths,
                priority);
        }

        private void LogScopeClassificationIfEnabled(BrowserBootstrapFilteringOptions options, string traceIdentifier, string observedPath)
        {
            LogLevel level = options.LogLevelBootstrapScopePaths;
            if (level == LogLevel.None || !_logger.IsEnabled(level))
            {
                return;
            }

            _logger.Log(
                level,
                "{Middleware} action={Action} observed={Observed} trace={Trace}",
                () => nameof(BrowserBootstrapFiltering),
                () => "BootstrapScope",
                () => observedPath,
                () => traceIdentifier);
        }

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

        private static bool HasBootstrapAttemptFlag(HttpContext context)
        {
            if (context.Request.Query.TryGetValue(BootstrapAttemptFlagKey, out StringValues values) && values.Count > 0)
            {
                string v = values[0] ?? string.Empty;
                return string.Equals(v, BootstrapAttemptFlagValue, StringComparison.Ordinal);
            }

            return false;
        }

        private static void RemoveBootstrapAttemptFlagFromQuery(HttpContext context)
        {
            if (!context.Request.Query.TryGetValue(BootstrapAttemptFlagKey, out _))
            {
                return;
            }

            var sb = new StringBuilder(128);
            bool first = true;

            foreach (var kvp in context.Request.Query)
            {
                if (string.Equals(kvp.Key, BootstrapAttemptFlagKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (string v in kvp.Value)
                {
                    sb.Append(first ? '?' : '&');
                    first = false;
                    sb.Append(Uri.EscapeDataString(kvp.Key));
                    sb.Append('=');
                    sb.Append(Uri.EscapeDataString(v ?? string.Empty));
                }
            }

            context.Request.QueryString = first ? QueryString.Empty : new QueryString(sb.ToString());
        }

        private static string BuildOriginalTargetOrThrow(HttpContext context)
        {
            string path = context.Request.Path.HasValue ? context.Request.Path.Value! : string.Empty;
            string query = context.Request.QueryString.HasValue ? context.Request.QueryString.Value! : string.Empty;

            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            {
                // Reviewer note: should never happen for normal ASP.NET Core requests, but enforce invariants.
                throw new InvalidOperationException("Request path is not a safe local path.");
            }

            if (query.Contains('\r', StringComparison.Ordinal) || query.Contains('\n', StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Request query is not header-safe.");
            }

            return path + query;
        }

        private static bool TryGetSafeOutcomeTarget(HttpContext context, out string target)
        {
            target = string.Empty;

            if (!context.Request.Query.TryGetValue(OutcomeTargetQueryKey, out StringValues values) || values.Count == 0)
            {
                return false;
            }

            string candidate = values[0] ?? string.Empty;

            if (!IsSafeLocalTarget(candidate))
            {
                return false;
            }

            // Prevent obvious infinite loops if someone points back into the outcome endpoints.
            if (candidate.StartsWith(BootstrapContinuePath, StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(BootstrapFailurePath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            target = candidate;
            return true;
        }

        private static bool IsSafeLocalTarget(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            // Must be an absolute-path reference within the same host.
            if (!candidate.StartsWith("/", StringComparison.Ordinal))
            {
                return false;
            }

            // Disallow scheme-relative.
            if (candidate.StartsWith("//", StringComparison.Ordinal))
            {
                return false;
            }

            // Basic header-safety.
            if (candidate.Contains('\r', StringComparison.Ordinal) || candidate.Contains('\n', StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private static Task WriteBootstrapAttemptHtmlAsync(HttpContext context, BrowserBootstrapFilteringOptions options, string originalTarget)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/html; charset=utf-8";

            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";

            string html = BuildBootstrapAttemptHtml(options, context.TraceIdentifier, originalTarget);
            return context.Response.WriteAsync(html);
        }

        private static string BuildBootstrapAttemptHtml(BrowserBootstrapFilteringOptions options, string traceIdentifier, string originalTarget)
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

            string target = JsEscape(originalTarget);

            string attemptKey = JsEscape(BootstrapAttemptFlagKey);
            string attemptValue = JsEscape(BootstrapAttemptFlagValue);

            int maxAgeSeconds = (int)Math.Clamp(options.CookieMaxAge.TotalSeconds, 60, int.MaxValue);
            string trace = JsEscape(traceIdentifier);

            var sb = new StringBuilder(1500);

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
            sb.AppendLine("    var target = '" + target + "';");
            sb.AppendLine("    var toOk = encodeURIComponent(target);");
            sb.AppendLine("    var targetFail = target + (target.indexOf('?') >= 0 ? '&' : '?') + '" + attemptKey + "=" + attemptValue + "';");
            sb.AppendLine("    var toFail = encodeURIComponent(targetFail);");
            sb.AppendLine("    try {");
            sb.AppendLine("      var secure = (location.protocol === 'https:') ? '; Secure' : '';");
            sb.AppendLine("      document.cookie = '" + cookieName + "=" + cookieValue + "; Path=/; Max-Age=" + maxAgeSeconds + "; SameSite=Lax' + secure;");
            sb.AppendLine("    } catch (e) { }");
            sb.AppendLine("    if (hasCookie('" + cookieName + "','" + cookieValue + "')) {");
            sb.AppendLine("      location.replace('" + continuePath + "?" + OutcomeTargetQueryKey + "=' + toOk);");
            sb.AppendLine("    } else {");
            sb.AppendLine("      location.replace('" + failPath + "?" + OutcomeTargetQueryKey + "=' + toFail);");
            sb.AppendLine("    }");
            sb.AppendLine("  })();");
            sb.AppendLine("  </script>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }
    }
}
