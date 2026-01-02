using System;
using System.Net;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.GenericExtensions.HttpResponseExtensions;
using Eigenverft.Routed.RequestFilters.GenericExtensions.IPAddressExtensions;
using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.DevelopmentUnlocker
{
    /// <summary>
    /// Middleware that removes stored filtering events for a remote IP address when a configured unlock endpoint is reached.
    /// </summary>
    /// <remarks>
    /// Reviewer note: Two request shapes are supported:
    /// <list type="bullet">
    /// <item><description><c>{EndpointPath}</c> unlocks the caller's normalized remote IP (from <see cref="RemoteIpAddressContextMiddleware"/>).</description></item>
    /// <item><description><c>{EndpointPath}/{ip}</c> unlocks the specified IP (parsed and normalized via <see cref="IPAddressExtensions.GetIpInfo(System.Net.IPAddress?)"/>).</description></item>
    /// </list>
    /// </remarks>
    public sealed class DevelopmentUnlocker
    {
        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<DevelopmentUnlocker> _logger;
        private readonly IOptionsMonitor<DevelopmentUnlockerOptions> _optionsMonitor;
        private readonly IFilteringEventStorage _filteringEventStorage;

        /// <summary>
        /// Initializes a new instance of the <see cref="DevelopmentUnlocker"/> class.
        /// </summary>
        /// <param name="nextMiddleware">The next middleware in the pipeline.</param>
        /// <param name="logger">The deferred logger instance.</param>
        /// <param name="optionsMonitor">The options monitor for <see cref="DevelopmentUnlockerOptions"/>.</param>
        /// <param name="filteringEventStorage">The central filtering event storage.</param>
        public DevelopmentUnlocker(
            RequestDelegate nextMiddleware,
            IDeferredLogger<DevelopmentUnlocker> logger,
            IOptionsMonitor<DevelopmentUnlockerOptions> optionsMonitor,
            IFilteringEventStorage filteringEventStorage)
        {
            _next = nextMiddleware ?? throw new ArgumentNullException(nameof(nextMiddleware));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _filteringEventStorage = filteringEventStorage ?? throw new ArgumentNullException(nameof(filteringEventStorage));

            _optionsMonitor.OnChange(_ => _logger.LogDebug("Configuration for {MiddlewareName} updated.", () => nameof(DevelopmentUnlocker)));
        }

        /// <summary>
        /// Processes the current request and triggers an unlock when the configured endpoint is reached.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            DevelopmentUnlockerOptions options = _optionsMonitor.CurrentValue;

            if (!options.Enabled)
            {
                await _next(context);
                return;
            }

            if (!TryGetUnlockOverrideSegment(context, options, out string? ipOverrideSegment, out bool isUnlockRequest))
            {
                // Reviewer note: invalid format (e.g., too many segments) -> treat as non-match (falls through).
                await _next(context);
                return;
            }

            if (!isUnlockRequest)
            {
                await _next(context);
                return;
            }

            // Reviewer note: caller IP is already normalized and stored by RemoteIpAddressContextMiddleware.
            string callerRemoteIp = context.GetRemoteIpAddress();

            string targetRemoteIp = callerRemoteIp;
            bool isOverride = false;

            if (!string.IsNullOrWhiteSpace(ipOverrideSegment))
            {
                if (!TryParseAndNormalizeIp(ipOverrideSegment!, out string normalizedOverrideIp))
                {
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.Log(
                            LogLevel.Warning,
                            "Development unlock requested with invalid IP segment {IpSegment} via {Method} {Path} from {CallerRemoteIpAddress}.",
                            () => ipOverrideSegment!,
                            () => context.Request.Method,
                            () => context.Request.Path.Value ?? string.Empty,
                            () => callerRemoteIp);
                    }

                    await context.Response.WriteDefaultStatusCodeAnswerEx(StatusCodes.Status400BadRequest);
                    return;
                }

                targetRemoteIp = normalizedOverrideIp;
                isOverride = true;
            }

            if (options.LogLevelUnlock != LogLevel.None && _logger.IsEnabled(options.LogLevelUnlock))
            {
                _logger.Log(
                    options.LogLevelUnlock,
                    "Development unlock triggered via {Method} {Path}; caller {CallerRemoteIpAddress}; target {TargetRemoteIpAddress}; override {IsOverride}.",
                    () => context.Request.Method,
                    () => context.Request.Path.Value ?? string.Empty,
                    () => callerRemoteIp,
                    () => targetRemoteIp,
                    () => isOverride);
            }

            await _filteringEventStorage.RemoveByRemoteIpAddressAsync(targetRemoteIp);

            await context.Response.WriteDefaultStatusCodeAnswerEx(StatusCodes.Status200OK);
        }

        /// <summary>
        /// Determines whether the request targets the unlock endpoint and extracts an optional IP override segment.
        /// </summary>
        /// <remarks>
        /// Reviewer note: Accepts:
        /// <list type="bullet">
        /// <item><description><c>{EndpointPath}</c> and <c>{EndpointPath}/</c></description></item>
        /// <item><description><c>{EndpointPath}/{segment}</c> where <c>{segment}</c> contains no additional slashes</description></item>
        /// </list>
        /// Anything with more than one extra segment is treated as a non-match.
        /// </remarks>
        /// <param name="context">The HTTP context.</param>
        /// <param name="options">The current options.</param>
        /// <param name="ipOverrideSegment">The raw override segment (not yet validated) or null.</param>
        /// <param name="isUnlockRequest">True when the request is an unlock request; otherwise false.</param>
        /// <returns>
        /// True when the path format is acceptable (either match or non-match); false when the request is malformed for this middleware.
        /// </returns>
        private static bool TryGetUnlockOverrideSegment(HttpContext context, DevelopmentUnlockerOptions options, out string? ipOverrideSegment, out bool isUnlockRequest)
        {
            ipOverrideSegment = null;
            isUnlockRequest = false;

            if (context == null) return true;
            if (options == null) return true;

            string configured = NormalizeConfiguredEndpointPath(options.EndpointPath);
            if (configured.Length == 0) return true;

            string requestPathRaw = (context.Request.Path.Value ?? string.Empty).Trim();
            requestPathRaw = requestPathRaw.TrimEnd('/');

            // /__dev/unlock
            if (requestPathRaw.Equals(configured, StringComparison.OrdinalIgnoreCase))
            {
                isUnlockRequest = true;
                return true;
            }

            // /__dev/unlock/{something}
            string prefix = configured + "/";
            if (!requestPathRaw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string remainder = requestPathRaw.Substring(prefix.Length);
            remainder = remainder.Trim('/');

            // allow "/__dev/unlock/" as well
            if (remainder.Length == 0)
            {
                isUnlockRequest = true;
                return true;
            }

            // reject multiple segments (e.g. /__dev/unlock/a/b)
            if (remainder.Contains("/", StringComparison.Ordinal))
            {
                // Reviewer note: treat as non-match rather than throwing; this keeps behavior predictable.
                return false;
            }

            ipOverrideSegment = remainder;
            isUnlockRequest = true;
            return true;
        }

        /// <summary>
        /// Normalizes the configured endpoint path into a canonical form.
        /// </summary>
        /// <param name="endpointPath">The configured path.</param>
        /// <returns>A normalized path that starts with <c>/</c> and has no trailing <c>/</c>.</returns>
        private static string NormalizeConfiguredEndpointPath(string? endpointPath)
        {
            string endpointPathRaw = endpointPath ?? string.Empty;
            endpointPathRaw = endpointPathRaw.Trim();

            if (endpointPathRaw.Length == 0) return string.Empty;

            if (!endpointPathRaw.StartsWith("/", StringComparison.Ordinal))
            {
                endpointPathRaw = "/" + endpointPathRaw;
            }

            endpointPathRaw = endpointPathRaw.TrimEnd('/');
            return endpointPathRaw;
        }

        /// <summary>
        /// Parses an IP string and normalizes it using <see cref="IPAddressExtensions.GetIpInfo(System.Net.IPAddress?)"/>.
        /// </summary>
        /// <remarks>
        /// Reviewer note: This is only used for the optional override segment. The caller IP is already normalized
        /// by <see cref="RemoteIpAddressContextMiddleware"/>.
        /// </remarks>
        /// <param name="rawSegment">Raw path segment that should represent an IP.</param>
        /// <param name="normalizedIp">Normalized IP suitable for storage comparison.</param>
        /// <returns><c>true</c> when parsing and normalization succeed; otherwise <c>false</c>.</returns>
        private static bool TryParseAndNormalizeIp(string rawSegment, out string normalizedIp)
        {
            normalizedIp = string.Empty;

            if (string.IsNullOrWhiteSpace(rawSegment))
            {
                return false;
            }

            // Reviewer note: be forgiving for pasted IPv6 bracket form.
            string candidate = rawSegment.Trim();
            if (candidate.Length >= 2 && candidate[0] == '[' && candidate[^1] == ']')
            {
                candidate = candidate.Substring(1, candidate.Length - 2).Trim();
            }

            // Reviewer note: decode percent-escaped characters (e.g. %25 in scope IDs).
            candidate = Uri.UnescapeDataString(candidate);

            if (!IPAddress.TryParse(candidate, out IPAddress? parsed))
            {
                return false;
            }

            var (_, remoteIp) = parsed.GetIpInfo(); // <-- here is your GetIpInfo() call
            if (string.IsNullOrWhiteSpace(remoteIp))
            {
                return false;
            }

            normalizedIp = remoteIp!;
            return true;
        }
    }
}
