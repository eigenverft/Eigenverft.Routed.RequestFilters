using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Eigenverft.Routed.RequestFilters.Middleware.RequestLogging
{
    /// <summary>
    /// Middleware that logs request/response details unless the request matches configured ignore patterns.
    /// </summary>
    /// <remarks>
    /// This middleware is intended primarily for debugging.
    /// It produces:
    /// - A single decision log line (ignored vs logged, plus reason).
    /// - Request/response logs (only when not ignored).
    /// </remarks>
    public sealed class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<RequestLoggingMiddleware> _logger;
        private readonly IOptionsMonitor<RequestLoggingOptions> _optionsMonitor;

        /// <summary>
        /// Initializes a new instance of the <see cref="RequestLoggingMiddleware"/> class.
        /// </summary>
        /// <param name="next">The next middleware in the pipeline.</param>
        /// <param name="logger">The deferred logger instance.</param>
        /// <param name="optionsMonitor">The options monitor for <see cref="RequestLoggingOptions"/>.</param>
        public RequestLoggingMiddleware(RequestDelegate next, IDeferredLogger<RequestLoggingMiddleware> logger, IOptionsMonitor<RequestLoggingOptions> optionsMonitor)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));

            _optionsMonitor.OnChange(_ => _logger.LogDebug("Configuration for {MiddlewareName} updated.", () => nameof(RequestLoggingMiddleware)));
        }

        /// <summary>
        /// Processes the current request and emits decision + request/response logs according to configuration.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            LoggingDecision decision = await InvokeRequestTasksAsync(context);

            var startTimeUtc = DateTime.UtcNow;

            try
            {
                await _next(context);
            }
            catch (OperationCanceledException)
            {
                if (!decision.IsIgnored)
                {
                    LogAtResolvedLevel(decision.Options.LogLevelLogging, "OperationCanceledException");
                }
            }
            catch (Exception ex)
            {
                if (!decision.IsIgnored)
                {
                    _logger.LogError(ex, "An exception occurred while processing the response logging.");
                }
            }

            await InvokeResponseTasksAsync(context, startTimeUtc, decision);
        }

        /// <summary>
        /// Runs request-side tasks:
        /// - determine ignore decision
        /// - log the decision line
        /// - log the request (only when not ignored)
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>The resolved decision.</returns>
        private Task<LoggingDecision> InvokeRequestTasksAsync(HttpContext context)
        {
            RequestLoggingOptions options = _optionsMonitor.CurrentValue ?? new RequestLoggingOptions();

            if (!options.IsEnabled)
            {
                return Task.FromResult(new LoggingDecision(options, isIgnored: true, reason: "Disabled"));
            }

            bool isIgnored = ShouldIgnore(context, options, out string ignoreReason);

            LogDecision(options, context, isIgnored, ignoreReason);

            if (!isIgnored)
            {
                LogLevel level = options.LogLevelLogging;
                if (level != LogLevel.None && _logger.IsEnabled(level))
                {
                    string requestLog = BuildRequestLog(context);
                    _logger.Log(level, "Request: {Request}", () => requestLog);
                }
            }

            return Task.FromResult(new LoggingDecision(options, isIgnored, ignoreReason));
        }

        /// <summary>
        /// Registers response-side logging via <see cref="HttpResponse.OnCompleted(Func{Task})"/>.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <param name="startTimeUtc">The request start timestamp (UTC).</param>
        /// <param name="decision">The resolved decision from request-side evaluation.</param>
        private Task InvokeResponseTasksAsync(HttpContext context, DateTime startTimeUtc, LoggingDecision decision)
        {
            if (decision.IsIgnored)
            {
                return Task.CompletedTask;
            }

            RequestLoggingOptions options = decision.Options;

            LogLevel level = options.LogLevelLogging;
            if (level == LogLevel.None || !_logger.IsEnabled(level))
            {
                return Task.CompletedTask;
            }

            context.Response.OnCompleted(() =>
            {
                try
                {
                    string responseLog = BuildResponseLog(context, startTimeUtc);
                    _logger.Log(level, "Response: {Response}", () => responseLog);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred while processing the response logging after completion.");
                }

                return Task.CompletedTask;
            });

            return Task.CompletedTask;
        }

        /// <summary>
        /// Holds the decision results for a single request.
        /// </summary>
        private readonly struct LoggingDecision
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="LoggingDecision"/> struct.
            /// </summary>
            /// <param name="options">Resolved options snapshot.</param>
            /// <param name="isIgnored">Whether request/response logs should be skipped.</param>
            /// <param name="reason">Decision reason token.</param>
            public LoggingDecision(RequestLoggingOptions options, bool isIgnored, string reason)
            {
                Options = options ?? new RequestLoggingOptions();
                IsIgnored = isIgnored;
                Reason = reason ?? string.Empty;
            }

            /// <summary>
            /// Gets the resolved options snapshot for this request.
            /// </summary>
            public RequestLoggingOptions Options { get; }

            /// <summary>
            /// Gets whether request/response logs should be skipped.
            /// </summary>
            public bool IsIgnored { get; }

            /// <summary>
            /// Gets the decision reason token (for example <c>IgnoreRemoteIpPatterns</c>).
            /// </summary>
            public string Reason { get; }
        }

        /// <summary>
        /// Writes a single, stable state log line for the ignore decision.
        /// </summary>
        /// <param name="options">Resolved options snapshot.</param>
        /// <param name="context">The current http context.</param>
        /// <param name="isIgnored">Whether the request was ignored.</param>
        /// <param name="reason">Decision reason token.</param>
        private void LogDecision(RequestLoggingOptions options, HttpContext context, bool isIgnored, string reason)
        {
            LogLevel level = options.LogLevelDecision;
            if (level == LogLevel.None || !_logger.IsEnabled(level))
            {
                return;
            }

            string decision = isIgnored ? "Ignored" : "Logged";

            _logger.Log(
                level,
                "{Middleware} decision={Decision} reason={Reason} remote={Remote} ua={UserAgent} trace={Trace}",
                () => nameof(RequestLoggingMiddleware),
                () => decision,
                () => (reason ?? string.Empty),
                () => context.GetRemoteIpAddress(),
                () => TryGetUserAgent(context),
                () => context.TraceIdentifier
            );
        }

        /// <summary>
        /// Determines whether request/response logging should be skipped for the current request.
        /// </summary>
        /// <remarks>
        /// Matching is intentionally fixed to case-insensitive (<see langword="false"/>) to keep configuration simple.
        /// A match in either ignore list (remote ip or user-agent) causes logging to be skipped.
        /// </remarks>
        /// <param name="context">The current http context.</param>
        /// <param name="options">The resolved options.</param>
        /// <param name="reason">A short token indicating which ignore list matched.</param>
        /// <returns><see langword="true"/> when logging should be skipped; otherwise <see langword="false"/>.</returns>
        private static bool ShouldIgnore(HttpContext context, RequestLoggingOptions options, out string reason)
        {
            reason = string.Empty;

            string remoteIp = context.GetRemoteIpAddress();
            string userAgent = TryGetUserAgent(context);

            const bool caseSensitive = false;
            FilterPriority filterPriority = FilterPriority.Whitelist;

            // Remote IP ignore: treat IgnoreRemoteIpPatterns as the "Whitelist" that triggers ignoring.
            string[]? whitelist = options.IgnoreRemoteIpPatterns;
            string[]? blacklist = null;

            string observed = remoteIp;

            FilterMatchKind matchKind = FilterClassifier.Classify(observed, whitelist, blacklist, caseSensitive, filterPriority);

            if (matchKind == FilterMatchKind.Whitelist)
            {
                reason = "IgnoreRemoteIpPatterns";
                return true;
            }

            // User-Agent ignore: treat IgnoreUserAgentPatterns as the "Whitelist" that triggers ignoring.
            whitelist = options.IgnoreUserAgentPatterns;
            blacklist = null;

            observed = userAgent;

            matchKind = FilterClassifier.Classify(observed, whitelist, blacklist, caseSensitive, filterPriority);

            if (matchKind == FilterMatchKind.Whitelist)
            {
                reason = "IgnoreUserAgentPatterns";
                return true;
            }

            return false;
        }

        private const int LogKeyPad = 26; // Keep alignment stable.

        /// <summary>
        /// Builds the request log text for the current request.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>A formatted request log string.</returns>
        private static string BuildRequestLog(HttpContext context)
        {
            var sb = new StringBuilder(2048);
            sb.AppendLine();

            AppendLine(sb, "Log.Type", "Request");

            AppendLine(sb, "RemoteIpContext.ElapsedMs", Safe(() => TryGetRemoteIpContextElapsedMs(context)));

            AppendLine(sb, "Connection.Id", Safe(() => context.Connection.Id));
            AppendLine(sb, "Trace.Id", Safe(() => context.TraceIdentifier));

            AppendLine(sb, "HTTP.Method", Safe(() => context.Request.Method));
            AppendLine(sb, "HTTP.Protocol", Safe(() => context.Request.Protocol));
            AppendLine(sb, "HTTP.Scheme", Safe(() => context.Request.Scheme));
            AppendLine(sb, "HTTP.Host", Safe(() => context.Request.Host.Value));
            AppendLine(sb, "HTTP.PathBase", Safe(() => context.Request.PathBase.Value));
            AppendLine(sb, "HTTP.Path", Safe(() => context.Request.Path.Value));
            AppendLine(sb, "HTTP.QueryString", Safe(() => context.Request.QueryString.Value));
            AppendLine(sb, "HTTP.DisplayUrl", Safe(() => context.Request.GetDisplayUrl()));

            AppendTlsProtocolLine(context, sb);

            AppendLine(sb, "Connection.RemoteEndpoint", Safe(() =>
            {
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
                var port = context.Connection.RemotePort.ToString();
                return string.IsNullOrWhiteSpace(ip) ? string.Empty : $"{ip}:{port}";
            }));

            AppendLine(sb, "Connection.LocalEndpoint", Safe(() =>
            {
                var ip = context.Connection.LocalIpAddress?.ToString() ?? string.Empty;
                var port = context.Connection.LocalPort.ToString();
                return string.IsNullOrWhiteSpace(ip) ? string.Empty : $"{ip}:{port}";
            }));

            AppendReverseDnsLine(Safe(() => context.Connection.RemoteIpAddress?.ToString() ?? string.Empty), sb);

            AppendLine(sb, "HTTP.UserAgent", Safe(() => TryGetHeader(context, "User-Agent")));
            AppendLine(sb, "HTTP.ContentType", Safe(() => context.Request.ContentType ?? string.Empty));
            AppendLine(sb, "HTTP.ContentLength", Safe(() => context.Request.ContentLength?.ToString() ?? string.Empty));

            AppendEndpointLine(context, sb);

            // Headers (sorted for stable output)
            foreach (var header in context.Request.Headers.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
            {
                AppendLine(sb, "Header.Request", Safe(() => $"{header.Key} = {header.Value}"));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Builds the response log text for the current request.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <param name="startTimeUtc">The request start timestamp (UTC).</param>
        /// <returns>A formatted response log string.</returns>
        private static string BuildResponseLog(HttpContext context, DateTime startTimeUtc)
        {
            var sb = new StringBuilder(1024);
            sb.AppendLine();

            AppendLine(sb, "Log.Type", "Response");

            AppendLine(sb, "Connection.Id", Safe(() => context.Connection.Id));
            AppendLine(sb, "Trace.Id", Safe(() => context.TraceIdentifier));

            AppendLine(sb, "HTTP.StatusCode", Safe(() => context.Response.StatusCode.ToString()));
            AppendLine(sb, "HTTP.DurationMs", Safe(() => (DateTime.UtcNow - startTimeUtc).TotalMilliseconds.ToString("0.###")));
            AppendLine(sb, "HTTP.ContentType", Safe(() => context.Response.ContentType ?? string.Empty));

            // Content-Length may not be set; also can live in headers or property depending on pipeline.
            AppendLine(sb, "HTTP.ContentLength", Safe(() =>
            {
                if (context.Response.ContentLength.HasValue)
                {
                    return context.Response.ContentLength.Value.ToString();
                }

                if (context.Response.Headers.TryGetValue("Content-Length", out var cl))
                {
                    return cl.ToString();
                }

                return string.Empty;
            }));

            // Headers (sorted for stable output)
            foreach (var header in context.Response.Headers.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
            {
                AppendLine(sb, "Header.Response", Safe(() => $"{header.Key} = {header.Value}"));
            }

            return sb.ToString();
        }


        /// <summary>
        /// Tries to append a TLS protocol line when the request is HTTPS.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <param name="sb">The target string builder.</param>
        private static void AppendTlsProtocolLine(HttpContext context, StringBuilder sb)
        {
            if (!context.Request.IsHttps)
            {
                return;
            }

            var tlsFeature = context.Features.Get<ITlsHandshakeFeature>();

            string protocolText = tlsFeature is null
                ? "Unknown handshake"
                : tlsFeature.Protocol.ToString();

            AppendLine(sb, "TLS Protocol", protocolText);
        }

        /// <summary>
        /// Tries to resolve and append reverse DNS host name information for a remote IP address.
        /// </summary>
        /// <param name="remoteIp">Remote ip address string.</param>
        /// <param name="sb">The target string builder.</param>
        private static void AppendReverseDnsLine(string remoteIp, StringBuilder sb)
        {
            if (string.IsNullOrWhiteSpace(remoteIp))
            {
                return;
            }

            // Reverse DNS can be slow; keep it fail-safe.
            var host = Safe(() =>
            {
                try
                {
                    var dns = Dns.GetHostEntry(remoteIp);
                    return dns.HostName ?? "Unresolvable";
                }
                catch
                {
                    return "Unresolvable";
                }
            });

            AppendLine(sb, "RemoteHost", host);
        }

        /// <summary>
        /// Returns the request User-Agent header as a string or an empty string when missing.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>User-Agent string or empty string.</returns>
        private static string TryGetUserAgent(HttpContext context)
        {
            return Safe(() => TryGetHeader(context, "User-Agent"));
        }

        /// <summary>
        /// Appends an endpoint summary line (route endpoint display name + route values) when available.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <param name="sb">The target string builder.</param>
        private static void AppendEndpointLine(HttpContext context, StringBuilder sb)
        {
            var endpointName = Safe(() => context.GetEndpoint()?.DisplayName ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(endpointName))
            {
                AppendLine(sb, "Endpoint", endpointName);
            }

            // Route values can throw in odd cases; keep it safe.
            var routeValues = Safe(() =>
            {
                RouteValueDictionary? rv = context.Request.RouteValues;
                if (rv == null || rv.Count == 0) return string.Empty;

                return string.Join(", ", rv.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            });

            if (!string.IsNullOrWhiteSpace(routeValues))
            {
                AppendLine(sb, "RouteValues", routeValues);
            }
        }

        /// <summary>
        /// Appends a single aligned key/value line.
        /// </summary>
        /// <param name="sb">The target string builder.</param>
        /// <param name="key">The key label.</param>
        /// <param name="value">The value.</param>
        private static void AppendLine(StringBuilder sb, string key, string value)
        {
            key ??= string.Empty;
            value ??= string.Empty;

            // Classic style alignment: "Key:     Value"
            sb.Append(key);
            sb.Append(':');

            int pad = LogKeyPad - key.Length;
            if (pad < 1) pad = 1;
            sb.Append(' ', pad);

            sb.AppendLine(value);
        }

        /// <summary>
        /// Tries to read a header value as a string, returning empty when missing.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <param name="headerName">Header name.</param>
        /// <returns>Header value or empty string.</returns>
        private static string TryGetHeader(HttpContext context, string headerName)
        {
            if (context.Request.Headers.TryGetValue(headerName, out StringValues values))
            {
                return values.ToString();
            }

            return string.Empty;
        }

        /// <summary>
        /// Executes a value factory and converts exceptions into a readable placeholder.
        /// </summary>
        /// <param name="getValue">Value factory.</param>
        /// <returns>The produced value or an error placeholder.</returns>
        private static string Safe(Func<string?> getValue)
        {
            try
            {
                return getValue?.Invoke() ?? string.Empty;
            }
            catch (Exception ex)
            {
                return $"<error:{ex.GetType().Name}>";
            }
        }

        /// <summary>
        /// Logs a simple message at the provided level if enabled.
        /// </summary>
        /// <param name="level">Target level.</param>
        /// <param name="message">Message to log.</param>
        private void LogAtResolvedLevel(LogLevel level, string message)
        {
            if (level == LogLevel.None || !_logger.IsEnabled(level))
            {
                return;
            }

            _logger.Log(level, "{Message}", () => message);
        }

        /// <summary>
        /// Computes the elapsed milliseconds since the remote-ip context start time was set, or returns empty when unavailable.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>Elapsed milliseconds as string or empty when start time is missing/unset.</returns>
        private static string TryGetRemoteIpContextElapsedMs(HttpContext context)
        {
            try
            {
                // Reviewer note:
                // This assumes your RemoteIpAddressContext exposes a getter for the start time.
                // If the logging middleware runs before that middleware, the start time may be default/unset.
                var startUtc = context.GetRemoteIpAddressStartTime();

                if (startUtc == default)
                {
                    return string.Empty;
                }

                var elapsed = DateTime.UtcNow - startUtc;

                // Guard against clock anomalies (shouldn't happen with UtcNow, but keep it safe).
                var ms = elapsed.TotalMilliseconds;
                if (ms < 0) ms = 0;

                return ms.ToString("0.###");
            }
            catch
            {
                return string.Empty;
            }
        }

    }
}
