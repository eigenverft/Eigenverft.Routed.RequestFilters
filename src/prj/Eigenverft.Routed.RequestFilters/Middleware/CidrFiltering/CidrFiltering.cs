using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

using Eigenverft.NetLib.Networking;
using Eigenverft.WebLib.Middleware.Primitives;
using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.NetLib.Logging.Deferred;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;
using Eigenverft.WebLib.ClientNetwork;
using Eigenverft.WebLib.Middleware.Primitives.Features;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.CidrFiltering
{
    /// <summary>
    /// Middleware that filters requests by comparing the remote IP address against configured CIDR allow/deny lists.
    /// </summary>
    /// <remarks>
    /// Evaluation rules:
    /// <list type="bullet">
    /// <item>Whitelist match: at least one CIDR in <see cref="CidrFilteringOptions.Whitelist"/> contains the remote IP.</item>
    /// <item>Blacklist match: at least one CIDR in <see cref="CidrFilteringOptions.Blacklist"/> contains the remote IP.</item>
    /// <item>If both match, <see cref="CidrFilteringOptions.FilterPriority"/> decides.</item>
    /// </list>
    /// Loopback requests are bypassed and always allowed.
    /// </remarks>
    public class CidrFiltering
    {
        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<CidrFiltering> _logger;
        private readonly IOptionsMonitor<CidrFilteringOptions> _optionsMonitor;
        private readonly IFilteringEventStorage _filteringEventStorage;

        /// <summary>
        /// Initializes a new instance of the <see cref="CidrFiltering"/> class.
        /// </summary>
        /// <param name="nextMiddleware">The next middleware in the pipeline.</param>
        /// <param name="logger">The deferred logger instance.</param>
        /// <param name="optionsMonitor">The options monitor for <see cref="CidrFilteringOptions"/>.</param>
        /// <param name="filteringEventStorage">The central filtering event storage.</param>
        public CidrFiltering(RequestDelegate nextMiddleware, IDeferredLogger<CidrFiltering> logger, IOptionsMonitor<CidrFilteringOptions> optionsMonitor, IFilteringEventStorage filteringEventStorage)
        {
            _next = nextMiddleware ?? throw new ArgumentNullException(nameof(nextMiddleware));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _filteringEventStorage = filteringEventStorage ?? throw new ArgumentNullException(nameof(filteringEventStorage));

            _optionsMonitor.OnChange(_ => _logger.LogDebug("Configuration for {MiddlewareName} updated.", () => nameof(CidrFiltering)));
        }

        /// <summary>
        /// Processes the current request by classifying the remote IP address and applying the configured policy.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            CidrFilteringOptions options = _optionsMonitor.CurrentValue;

            IPAddress remoteIp = context
                .GetRequiredFeature<IClientNetworkFeature>()
                .RemoteIpAddress;

            // Loopback bypass: never filter localhost traffic.
            if (IPAddress.IsLoopback(remoteIp))
            {
                _logger.LogDebug("Bypassing {MiddlewareName} for loopback remote IP '{RemoteIp}'.", () => nameof(CidrFiltering), () => remoteIp.ToString());
                await _next(context);
                return;
            }

            string observed = remoteIp.ToCanonicalString();

            FilterMatchKind matchKind = Classify(remoteIp, options);

            if (matchKind == FilterMatchKind.Whitelist)
            {
                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                    nameof(CidrFiltering),
                    context.TraceIdentifier,
                    matchKind,
                    isAllowed: true,
                    observed,
                    loggedForEvaluator: false,
                    options.LogLevelWhitelist,
                    options.LogLevelBlacklist,
                    options.LogLevelUnmatched);

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
                        EventSource = nameof(CidrFiltering),
                        MatchKind = matchKind,
                        RemoteIpAddress = context.GetRemoteIpAddress(),
                        ObservedValue = observed
                    });
                }

                bool isAllowed = options.AllowBlacklistedRequests;

                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                    nameof(CidrFiltering),
                    context.TraceIdentifier,
                    matchKind,
                    isAllowed,
                    observed,
                    options.RecordBlacklistedRequests,
                    options.LogLevelWhitelist,
                    options.LogLevelBlacklist,
                    options.LogLevelUnmatched);

                if (log.Level != LogLevel.None && _logger.IsEnabled(log.Level))
                {
                    _logger.Log(log.Level, log.MessageTemplate, log.Args);
                }

                if (isAllowed)
                {
                    await _next(context);
                    return;
                }

                await context.Response.WriteHtmlStatusResponseAsync(options.BlockStatusCode);
                return;
            }

            if (matchKind == FilterMatchKind.Unmatched)
            {
                if (options.RecordUnmatchedRequests)
                {
                    await _filteringEventStorage.StoreAsync(new FilteringEvent
                    {
                        TimestampUtc = DateTime.UtcNow,
                        EventSource = nameof(CidrFiltering),
                        MatchKind = matchKind,
                        RemoteIpAddress = context.GetRemoteIpAddress(),
                        ObservedValue = observed
                    });
                }

                bool isAllowed = options.AllowUnmatchedRequests;

                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                    nameof(CidrFiltering),
                    context.TraceIdentifier,
                    matchKind,
                    isAllowed,
                    observed,
                    options.RecordUnmatchedRequests,
                    options.LogLevelWhitelist,
                    options.LogLevelBlacklist,
                    options.LogLevelUnmatched);

                if (log.Level != LogLevel.None && _logger.IsEnabled(log.Level))
                {
                    _logger.Log(log.Level, log.MessageTemplate, log.Args);
                }

                if (isAllowed)
                {
                    await _next(context);
                    return;
                }

                await context.Response.WriteHtmlStatusResponseAsync(options.BlockStatusCode);
                return;
            }

            _logger.LogCritical(
                "ATTENTION: {MiddlewareName} received an unexpected {EnumType} value '{EnumValue}'. Your filtering logic was extended but this middleware was not updated. This should not happen.",
                () => nameof(CidrFiltering),
                () => nameof(FilterMatchKind),
                () => matchKind);

            await _next(context);
        }

        private static FilterMatchKind Classify(IPAddress remoteIp, CidrFilteringOptions options)
        {
            bool anyWhitelist = IsInList(remoteIp, options.Whitelist);
            bool anyBlacklist = IsInList(remoteIp, options.Blacklist);

            if (!anyWhitelist && !anyBlacklist)
            {
                return FilterMatchKind.Unmatched;
            }

            if (anyWhitelist && anyBlacklist)
            {
                return options.FilterPriority == FilterPriority.Whitelist
                    ? FilterMatchKind.Whitelist
                    : FilterMatchKind.Blacklist;
            }

            return anyWhitelist ? FilterMatchKind.Whitelist : FilterMatchKind.Blacklist;
        }

        private static bool IsInList(IPAddress ip, IEnumerable<string>? cidrList)
        {
            return ip.Matches(cidrList);
        }
    }
}
