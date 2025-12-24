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

namespace Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressFiltering
{
    /// <summary>
    /// Middleware that filters http requests based on the remote ip address using configured whitelist and blacklist rules.
    /// </summary>
    public class RemoteIpAddressFiltering
    {
        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<RemoteIpAddressFiltering> _logger;
        private readonly IOptionsMonitor<RemoteIpAddressFilteringOptions> _optionsMonitor;
        private readonly IFilteringEventStorage _filteringEventStorage;

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteIpAddressFiltering"/> class.
        /// </summary>
        /// <param name="nextMiddleware">The next middleware in the pipeline.</param>
        /// <param name="logger">The deferred logger instance.</param>
        /// <param name="optionsMonitor">The options monitor for <see cref="RemoteIpAddressFilteringOptions"/>.</param>
        /// <param name="filteringEventStorage">The central filtering event storage.</param>
        public RemoteIpAddressFiltering(RequestDelegate nextMiddleware, IDeferredLogger<RemoteIpAddressFiltering> logger, IOptionsMonitor<RemoteIpAddressFilteringOptions> optionsMonitor, IFilteringEventStorage filteringEventStorage)
        {
            _next = nextMiddleware ?? throw new ArgumentNullException(nameof(nextMiddleware));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _filteringEventStorage = filteringEventStorage ?? throw new ArgumentNullException(nameof(filteringEventStorage));
            _optionsMonitor.OnChange(_ => _logger.LogDebug("Configuration for {MiddlewareName} updated.", () => nameof(RemoteIpAddressFiltering)));
        }

        /// <summary>
        /// Processes the current request by classifying the remote ip address and applying the configured policy.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            RemoteIpAddressFilteringOptions options = _optionsMonitor.CurrentValue;
            string observed = context.GetRemoteIpAddress();

            FilterMatchKind matchKind = FilterClassifier.Classify(observed, options.Whitelist, options.Blacklist, options.CaseSensitive, options.FilterPriority);

            if (matchKind == FilterMatchKind.Whitelist)
            {
                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(nameof(RemoteIpAddressFiltering), context.TraceIdentifier, matchKind, isAllowed: true, observed, loggedForEvaluator: false, options.LogLevelWhitelist, options.LogLevelBlacklist, options.LogLevelUnmatched);
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
                    await _filteringEventStorage.StoreAsync(new FilteringEvent { TimestampUtc = DateTime.UtcNow, EventSource = nameof(RemoteIpAddressFiltering), MatchKind = matchKind, RemoteIpAddress = observed, ObservedValue = observed });
                }

                bool isAllowed = options.AllowBlacklistedRequests;
                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(nameof(RemoteIpAddressFiltering), context.TraceIdentifier, matchKind, isAllowed, observed, options.RecordBlacklistedRequests, options.LogLevelWhitelist, options.LogLevelBlacklist, options.LogLevelUnmatched);
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

            if (matchKind == FilterMatchKind.Unmatched)
            {
                if (options.RecordUnmatchedRequests)
                {
                    await _filteringEventStorage.StoreAsync(new FilteringEvent { TimestampUtc = DateTime.UtcNow, EventSource = nameof(RemoteIpAddressFiltering), MatchKind = matchKind, RemoteIpAddress = observed, ObservedValue = observed });
                }

                bool isAllowed = options.AllowUnmatchedRequests;

                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(nameof(RemoteIpAddressFiltering), context.TraceIdentifier, matchKind, isAllowed, observed, options.RecordUnmatchedRequests, options.LogLevelWhitelist, options.LogLevelBlacklist, options.LogLevelUnmatched);

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

            _logger.LogCritical("ATTENTION: {MiddlewareName} received an unexpected {EnumType} value '{EnumValue}'. Your filtering logic was extended but this middleware was not updated. This should not happen.", () => nameof(RemoteIpAddressFiltering), () => nameof(FilterMatchKind), () => matchKind);
            await _next(context);
        }
    }
}