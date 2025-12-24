using System;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.GenericExtensions.HttpResponseExtensions;
using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.RequestUrlFiltering
{
    /// <summary>
    /// Middleware that filters HTTP requests based on the request URL path using configured whitelist and blacklist rules.
    /// </summary>
    /// <remarks>
    /// The observed value is the request URI local path (for example <c>/api/v1/users</c>).
    /// If the full request URI cannot be constructed, the observed value is <see cref="string.Empty"/> and is treated as blacklisted.
    /// </remarks>
    public class RequestUrlFiltering
    {
        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<RequestUrlFiltering> _logger;
        private readonly IOptionsMonitor<RequestUrlFilteringOptions> _optionsMonitor;
        private readonly IFilteringEventStorage _filteringEventStorage;

        /// <summary>
        /// Initializes a new instance of the <see cref="RequestUrlFiltering"/> class.
        /// </summary>
        /// <param name="nextMiddleware">The next middleware in the pipeline.</param>
        /// <param name="logger">The deferred logger instance.</param>
        /// <param name="optionsMonitor">The options monitor for <see cref="RequestUrlFilteringOptions"/>.</param>
        /// <param name="filteringEventStorage">The central filtering event storage.</param>
        public RequestUrlFiltering(
            RequestDelegate nextMiddleware,
            IDeferredLogger<RequestUrlFiltering> logger,
            IOptionsMonitor<RequestUrlFilteringOptions> optionsMonitor,
            IFilteringEventStorage filteringEventStorage)
        {
            _next = nextMiddleware ?? throw new ArgumentNullException(nameof(nextMiddleware));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _filteringEventStorage = filteringEventStorage ?? throw new ArgumentNullException(nameof(filteringEventStorage));

            _optionsMonitor.OnChange(_ => _logger.LogDebug("Configuration for {MiddlewareName} updated.", () => nameof(RequestUrlFiltering)));
        }

        /// <summary>
        /// Processes the current request by classifying the request URL path and applying the configured policy.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            RequestUrlFilteringOptions options = _optionsMonitor.CurrentValue;

            // Legacy behavior: build a full URI and filter its LocalPath.
            // If we cannot build a full URI, treat as disallowed (forced blacklist).
            Uri? fullUri = TryBuildFullRequestUri(context);
            string observed = fullUri?.LocalPath ?? string.Empty;

            // Old middleware forced empty path to disallowed.
            // We keep this deterministically as Blacklist, independent of list contents.
            FilterMatchKind matchKind = observed.Length == 0
                ? FilterMatchKind.Blacklist
                : FilterClassifier.Classify(observed, options.Whitelist, options.Blacklist, options.CaseSensitive, options.FilterPriority);

            if (matchKind == FilterMatchKind.Whitelist)
            {
                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                    nameof(RequestUrlFiltering),
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
                        EventSource = nameof(RequestUrlFiltering),
                        MatchKind = matchKind,
                        RemoteIpAddress = context.GetRemoteIpAddress(),
                        ObservedValue = observed
                    });
                }

                bool isAllowed = options.AllowBlacklistedRequests;

                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                    nameof(RequestUrlFiltering),
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
                        EventSource = nameof(RequestUrlFiltering),
                        MatchKind = matchKind,
                        RemoteIpAddress = context.GetRemoteIpAddress(),
                        ObservedValue = observed
                    });
                }

                bool isAllowed = options.AllowUnmatchedRequests;

                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                    nameof(RequestUrlFiltering),
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

                await context.Response.WriteDefaultStatusCodeAnswerEx(options.BlockStatusCode);
                return;
            }

            _logger.LogCritical(
                "ATTENTION: {MiddlewareName} received an unexpected {EnumType} value '{EnumValue}'. Your filtering logic was extended but this middleware was not updated. This should not happen.",
                () => nameof(RequestUrlFiltering),
                () => nameof(FilterMatchKind),
                () => matchKind);

            await _next(context);
        }

        private Uri? TryBuildFullRequestUri(HttpContext context)
        {
            try
            {
                HttpRequest request = context.Request;

                // If scheme/host are missing, UriBuilder cannot produce a meaningful absolute URI.
                if (string.IsNullOrEmpty(request.Scheme) || string.IsNullOrEmpty(request.Host.Host))
                {
                    return null;
                }

                var uriBuilder = new UriBuilder
                {
                    Scheme = request.Scheme,
                    Host = request.Host.Host,
                    Port = request.Host.Port ?? -1,
                    Path = request.PathBase.Add(request.Path).ToString(),
                    Query = request.QueryString.ToString()
                };

                return uriBuilder.Uri;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to build full request URI. {DisplayUrl}", () => context.Request.GetDisplayUrl());
                return null;
            }
        }
    }
}
