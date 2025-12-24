using System;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.GenericExtensions.HttpResponseExtensions;
using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.PathDepthFiltering
{
    /// <summary>
    /// Middleware that filters HTTP requests based on the depth of the request path (number of segments).
    /// </summary>
    /// <remarks>
    /// This middleware is a strict two-state classifier:
    /// depth within limit is treated as <see cref="FilterMatchKind.Whitelist"/>, depth exceeding the limit is treated as <see cref="FilterMatchKind.Blacklist"/>.
    /// There is no <see cref="FilterMatchKind.Unmatched"/> branch.
    /// </remarks>
    public class PathDepthFiltering
    {
        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<PathDepthFiltering> _logger;
        private readonly IOptionsMonitor<PathDepthFilteringOptions> _optionsMonitor;
        private readonly IFilteringEventStorage _filteringEventStorage;

        /// <summary>
        /// Initializes a new instance of the <see cref="PathDepthFiltering"/> class.
        /// </summary>
        public PathDepthFiltering(
            RequestDelegate nextMiddleware,
            IDeferredLogger<PathDepthFiltering> logger,
            IOptionsMonitor<PathDepthFilteringOptions> optionsMonitor,
            IFilteringEventStorage filteringEventStorage)
        {
            _next = nextMiddleware ?? throw new ArgumentNullException(nameof(nextMiddleware));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _filteringEventStorage = filteringEventStorage ?? throw new ArgumentNullException(nameof(filteringEventStorage));

            _optionsMonitor.OnChange(_ => _logger.LogDebug("Configuration for {MiddlewareName} updated.", () => nameof(PathDepthFiltering)));
        }

        /// <summary>
        /// Processes the current request by comparing the path depth against the configured limit.
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            PathDepthFilteringOptions options = _optionsMonitor.CurrentValue;

            string requestPath = context.Request.Path.Value ?? string.Empty;
            int depth = CalculatePathDepth(requestPath);

            // Observed value for logs/events: just the depth (stable and easy to query).
            string observed = depth.ToString();

            FilterMatchKind matchKind = depth <= options.PathDepthLimit
                ? FilterMatchKind.Whitelist
                : FilterMatchKind.Blacklist;

            if (matchKind == FilterMatchKind.Whitelist)
            {
                // No unmatched support here; pass LogLevel.None as the "unmatched" slot for the shared builder.
                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                    nameof(PathDepthFiltering),
                    context.TraceIdentifier,
                    matchKind,
                    isAllowed: true,
                    observed,
                    loggedForEvaluator: false,
                    options.LogLevelWhitelist,
                    options.LogLevelBlacklist,
                    LogLevel.None);

                if (log.Level != LogLevel.None && _logger.IsEnabled(log.Level))
                {
                    _logger.Log(log.Level, log.MessageTemplate, log.Args);
                }

                await _next(context);
                return;
            }

            if (matchKind == FilterMatchKind.Blacklist)
            {
                if (options.RecordBlacklistedRequests)
                {
                    await _filteringEventStorage.StoreAsync(new FilteringEvent
                    {
                        TimestampUtc = DateTime.UtcNow,
                        EventSource = nameof(PathDepthFiltering),
                        MatchKind = matchKind,
                        RemoteIpAddress = context.GetRemoteIpAddress(),
                        ObservedValue = $"{observed} (path='{requestPath}')"
                    });
                }

                bool isAllowed = options.AllowBlacklistedRequests;

                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                    nameof(PathDepthFiltering),
                    context.TraceIdentifier,
                    matchKind,
                    isAllowed,
                    observed,
                    options.RecordBlacklistedRequests,
                    options.LogLevelWhitelist,
                    options.LogLevelBlacklist,
                    LogLevel.None);

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
                return;
            }

            _logger.LogCritical(
                "ATTENTION: {MiddlewareName} received an unexpected {EnumType} value '{EnumValue}'. This should not happen.",
                () => nameof(PathDepthFiltering),
                () => nameof(FilterMatchKind),
                () => matchKind);

            await _next(context);
        }

        private static int CalculatePathDepth(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return 0;
            }

            string normalized = path;

            if (normalized.Length > 0 && normalized[0] == '/')
            {
                normalized = normalized.Substring(1);
            }

            if (normalized.Length > 0 && normalized[normalized.Length - 1] == '/')
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }

            if (normalized.Length == 0)
            {
                return 0;
            }

            // Keep empty entries: "/a//b" => ["a", "", "b"] => depth 3.
            return normalized.Split('/', StringSplitOptions.None).Length;
        }
    }
}
