using System;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.GenericExtensions.HttpResponseExtensions;
using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;

using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.TlsProtocolFiltering
{
    /// <summary>
    /// Middleware that filters HTTPS requests based on the negotiated TLS protocol using configured whitelist and blacklist rules.
    /// </summary>
    /// <remarks>
    /// This middleware intentionally does nothing for non-HTTPS requests.
    /// <para>
    /// For HTTPS requests, the TLS protocol is obtained from <see cref="ITlsHandshakeFeature"/> when available.
    /// If the feature is missing or reports an unspecified protocol, the observed value is normalized to <c>string.Empty</c>.
    /// </para>
    /// </remarks>
    public class TlsProtocolFiltering
    {
        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<TlsProtocolFiltering> _logger;
        private readonly IOptionsMonitor<TlsProtocolFilteringOptions> _optionsMonitor;
        private readonly IFilteringEventStorage _filteringEventStorage;

        /// <summary>
        /// Initializes a new instance of the <see cref="TlsProtocolFiltering"/> class.
        /// </summary>
        /// <param name="nextMiddleware">The next middleware in the pipeline.</param>
        /// <param name="logger">The deferred logger instance.</param>
        /// <param name="optionsMonitor">The options monitor for <see cref="TlsProtocolFilteringOptions"/>.</param>
        /// <param name="filteringEventStorage">The central filtering event storage.</param>
        public TlsProtocolFiltering(RequestDelegate nextMiddleware, IDeferredLogger<TlsProtocolFiltering> logger, IOptionsMonitor<TlsProtocolFilteringOptions> optionsMonitor, IFilteringEventStorage filteringEventStorage)
        {
            _next = nextMiddleware ?? throw new ArgumentNullException(nameof(nextMiddleware));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _filteringEventStorage = filteringEventStorage ?? throw new ArgumentNullException(nameof(filteringEventStorage));
            _optionsMonitor.OnChange(_ => _logger.LogDebug("Configuration for {MiddlewareName} updated.", () => nameof(TlsProtocolFiltering)));
        }

        /// <summary>
        /// Processes the current request by classifying the negotiated TLS protocol and applying the configured policy.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Requirement: non-HTTPS requests are not evaluated by this middleware.
            if (!context.Request.IsHttps)
            {
                await _next(context);
                return;
            }

            TlsProtocolFilteringOptions options = _optionsMonitor.CurrentValue;
            string observed = GetObservedTlsProtocolOrEmpty(context);

            FilterMatchKind matchKind = FilterClassifier.Classify(observed, options.Whitelist, options.Blacklist, options.CaseSensitive, options.FilterPriority);

            if (matchKind == FilterMatchKind.Whitelist)
            {
                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(nameof(TlsProtocolFiltering), context.TraceIdentifier, matchKind, isAllowed: true, observed, loggedForEvaluator: false, options.LogLevelWhitelist, options.LogLevelBlacklist, options.LogLevelUnmatched);
                if (log.Level != LogLevel.None && _logger.IsEnabled(log.Level)) _logger.Log(log.Level, log.MessageTemplate, log.Args);
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
                        EventSource = nameof(TlsProtocolFiltering),
                        MatchKind = matchKind,
                        RemoteIpAddress = context.GetRemoteIpAddress(),
                        ObservedValue = observed
                    });
                }

                bool isAllowed = options.AllowBlacklistedRequests;
                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(nameof(TlsProtocolFiltering), context.TraceIdentifier, matchKind, isAllowed, observed, options.RecordBlacklistedRequests, options.LogLevelWhitelist, options.LogLevelBlacklist, options.LogLevelUnmatched);
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
                    await _filteringEventStorage.StoreAsync(new FilteringEvent
                    {
                        TimestampUtc = DateTime.UtcNow,
                        EventSource = nameof(TlsProtocolFiltering),
                        MatchKind = matchKind,
                        RemoteIpAddress = context.GetRemoteIpAddress(),
                        ObservedValue = observed
                    });
                }

                bool isAllowed = options.AllowUnmatchedRequests;
                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(nameof(TlsProtocolFiltering), context.TraceIdentifier, matchKind, isAllowed, observed, options.RecordUnmatchedRequests, options.LogLevelWhitelist, options.LogLevelBlacklist, options.LogLevelUnmatched);
                if (log.Level != LogLevel.None && _logger.IsEnabled(log.Level)) _logger.Log(log.Level, log.MessageTemplate, log.Args);

                if (isAllowed)
                {
                    await _next(context);
                    return;
                }

                await context.Response.WriteDefaultStatusCodeAnswerEx(options.BlockStatusCode);
                return;
            }

            _logger.LogCritical(
                "ATTENTION: {MiddlewareName} received an unexpected {EnumType} value '{EnumValue}'. Your filtering logic was extended but this middleware was not updated. This should not happen.",
                () => nameof(TlsProtocolFiltering),
                () => nameof(FilterMatchKind),
                () => matchKind);

            await _next(context);
        }

        private static string GetObservedTlsProtocolOrEmpty(HttpContext context)
        {
            // Requirement: unknown should normalize to empty (not "Unknown handshake").
            ITlsHandshakeFeature? tlsFeature = context.Features.Get<ITlsHandshakeFeature>();
            if (tlsFeature is null) return string.Empty;

            // Protocol is an enum; treat 0/None as unknown.
            if ((int)tlsFeature.Protocol == 0) return string.Empty;

            return tlsFeature.Protocol.ToString() ?? string.Empty;
        }
    }
}