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

namespace Eigenverft.Routed.RequestFilters.Middleware.HttpProtocolFiltering
{
    /// <summary>
    /// Middleware that filters HTTP requests based on their protocol using configured whitelist and blacklist rules.
    /// </summary>
    public class HttpProtocolFiltering
    {
        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<HttpProtocolFiltering> _logger;
        private readonly IOptionsMonitor<HttpProtocolFilteringOptions> _optionsMonitor;
        private readonly IFilteringEventStorage _filteringEventStorage;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpProtocolFiltering"/> class.
        /// </summary>
        /// <param name="nextMiddleware">The next middleware in the pipeline.</param>
        /// <param name="logger">The deferred logger instance.</param>
        /// <param name="optionsMonitor">The options monitor for <see cref="HttpProtocolFilteringOptions"/>.</param>
        /// <param name="filteringEventStorage">The central filtering event storage.</param>
        public HttpProtocolFiltering(RequestDelegate nextMiddleware, IDeferredLogger<HttpProtocolFiltering> logger, IOptionsMonitor<HttpProtocolFilteringOptions> optionsMonitor, IFilteringEventStorage filteringEventStorage)
        {
            _next = nextMiddleware ?? throw new ArgumentNullException(nameof(nextMiddleware));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _filteringEventStorage = filteringEventStorage ?? throw new ArgumentNullException(nameof(filteringEventStorage));
            _optionsMonitor.OnChange(_ => _logger.LogDebug("Configuration for {MiddlewareName} updated.", () => nameof(HttpProtocolFiltering)));
        }

        /// <summary>
        /// Processes the current request by classifying the http protocol and applying the configured policy.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            HttpProtocolFilteringOptions options = _optionsMonitor.CurrentValue;
            string observed = context.Request.Protocol ?? string.Empty;

            FilterMatchKind matchKind = FilterClassifier.Classify(observed, options.Whitelist, options.Blacklist, options.CaseSensitive, options.FilterPriority);

            if (matchKind == FilterMatchKind.Whitelist)
            {
                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(nameof(HttpProtocolFiltering), context.TraceIdentifier, matchKind, isAllowed: true, observed, loggedForEvaluator: false, options.LogLevelWhitelist, options.LogLevelBlacklist, options.LogLevelUnmatched);
                if (log.Level != LogLevel.None && _logger.IsEnabled(log.Level)) _logger.Log(log.Level, log.MessageTemplate, log.Args);
                await _next(context);
                return;
            }

            if (matchKind == FilterMatchKind.Blacklist)
            {
                if (options.RecordBlacklistedRequests)
                {
                    await _filteringEventStorage.StoreAsync(new FilteringEvent { TimestampUtc = DateTime.UtcNow, EventSource = nameof(HttpProtocolFiltering), MatchKind = matchKind, RemoteIpAddress = context.GetRemoteIpAddress(), ObservedValue = observed });
                }

                bool isAllowed = options.AllowBlacklistedRequests;
                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(nameof(HttpProtocolFiltering), context.TraceIdentifier, matchKind, isAllowed, observed, options.RecordBlacklistedRequests, options.LogLevelWhitelist, options.LogLevelBlacklist, options.LogLevelUnmatched);
                if (log.Level != LogLevel.None && _logger.IsEnabled(log.Level)) _logger.Log(log.Level, log.MessageTemplate, log.Args);

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
                    await _filteringEventStorage.StoreAsync(new FilteringEvent { TimestampUtc = DateTime.UtcNow, EventSource = nameof(HttpProtocolFiltering), MatchKind = matchKind, RemoteIpAddress = context.GetRemoteIpAddress(), ObservedValue = observed });
                }

                bool isAllowed = options.AllowUnmatchedRequests;
                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(nameof(HttpProtocolFiltering), context.TraceIdentifier, matchKind, isAllowed, observed, options.RecordUnmatchedRequests, options.LogLevelWhitelist, options.LogLevelBlacklist, options.LogLevelUnmatched);
                if (log.Level != LogLevel.None && _logger.IsEnabled(log.Level)) _logger.Log(log.Level, log.MessageTemplate, log.Args);

                if (isAllowed)
                {
                    await _next(context);
                    return;
                }

                await context.Response.WriteDefaultStatusCodeAnswerEx(options.BlockStatusCode);
                return;
            }

            _logger.LogCritical("ATTENTION: {MiddlewareName} received an unexpected {EnumType} value '{EnumValue}'. Your filtering logic was extended but this middleware was not updated. This should not happen.", () => nameof(HttpProtocolFiltering), () => nameof(FilterMatchKind), () => matchKind);
            await _next(context);
        }
    }
}