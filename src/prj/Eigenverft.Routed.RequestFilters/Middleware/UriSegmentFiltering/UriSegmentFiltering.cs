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

namespace Eigenverft.Routed.RequestFilters.Middleware.UriSegmentFiltering
{
    /// <summary>
    /// Middleware that filters HTTP requests based on their URI path segments using configured whitelist and blacklist rules.
    /// </summary>
    /// <remarks>
    /// The middleware evaluates the decoded path segments (for example <c>/api/v1/users</c> becomes <c>api</c>, <c>v1</c>, <c>users</c>).
    /// A request is considered whitelisted when at least one segment matches the whitelist.
    /// A request is considered blacklisted when at least one segment matches the blacklist.
    /// If both apply, <see cref="UriSegmentFilteringOptions.FilterPriority"/> decides the outcome.
    /// <para>
    /// Hard rule (always on): paths that consist only of slashes and are longer than <c>/</c> (for example <c>//</c>, <c>///</c>, <c>/////</c>)
    /// are treated as <see cref="FilterMatchKind.Blacklist"/>. This is independent from the configured lists.
    /// </para>
    /// </remarks>
    public class UriSegmentFiltering
    {
        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<UriSegmentFiltering> _logger;
        private readonly IOptionsMonitor<UriSegmentFilteringOptions> _optionsMonitor;
        private readonly IFilteringEventStorage _filteringEventStorage;

        /// <summary>
        /// Initializes a new instance of the <see cref="UriSegmentFiltering"/> class.
        /// </summary>
        /// <param name="nextMiddleware">The next middleware in the pipeline.</param>
        /// <param name="logger">The deferred logger instance.</param>
        /// <param name="optionsMonitor">The options monitor for <see cref="UriSegmentFilteringOptions"/>.</param>
        /// <param name="filteringEventStorage">The central filtering event storage.</param>
        public UriSegmentFiltering(RequestDelegate nextMiddleware, IDeferredLogger<UriSegmentFiltering> logger, IOptionsMonitor<UriSegmentFilteringOptions> optionsMonitor, IFilteringEventStorage filteringEventStorage)
        {
            _next = nextMiddleware ?? throw new ArgumentNullException(nameof(nextMiddleware));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _filteringEventStorage = filteringEventStorage ?? throw new ArgumentNullException(nameof(filteringEventStorage));

            // Keep the pattern consistent with your other middlewares: show a debug message when options hot-reload changes.
            _optionsMonitor.OnChange(_ => _logger.LogDebug("Configuration for {MiddlewareName} updated.", () => nameof(UriSegmentFiltering)));
        }

        /// <summary>
        /// Processes the current request by classifying the request path segments and applying the configured policy.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            UriSegmentFilteringOptions options = _optionsMonitor.CurrentValue;

            // Use the raw path string for logging and for the "hard rule" check.
            // This string is also used when we cannot determine a specific segment to log.
            string displayPath = context.Request.Path.Value ?? string.Empty;

            // -----------------------------------------------------------------
            // 1) CLASSIFICATION (Hard rule + segment-based classification)
            // -----------------------------------------------------------------
            SegmentClassificationResult classification;

            // Hard rule:
            // Requests like "//", "///", "/////" (only slashes, longer than "/") are almost always probes / garbage.
            // We always treat them as Blacklist, independent from the configured lists.
            if (IsOnlySlashesLongerThanRoot(displayPath))
            {
                // observedSegment: null -> later we fall back to "displayPath" for logging and event storage.
                classification = new SegmentClassificationResult(FilterMatchKind.Blacklist, observedSegment: null);
            }
            else
            {
                // Normal case: split and decode segments, then evaluate against configured whitelist/blacklist.
                string[] segments = GetNormalizedSegments(context);
                classification = ClassifySegments(segments, options);
            }

            // Observed value used for logs/events:
            // - For Whitelist/Blacklist: first matching segment (if available)
            // - For Unmatched (and for the hard-rule blacklist): the full path
            string observed = classification.ObservedSegment ?? displayPath;

            FilterMatchKind matchKind = classification.MatchKind;

            // -----------------------------------------------------------------
            // 2) POLICY APPLICATION (same structure as your other middlewares)
            // -----------------------------------------------------------------
            if (matchKind == FilterMatchKind.Whitelist)
            {
                // Whitelist means "allowed" (always).
                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                    nameof(UriSegmentFiltering),
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
                // Blacklist means: record optionally, then allow/block based on config.
                if (options.RecordBlacklistedRequests)
                {
                    await _filteringEventStorage.StoreAsync(new FilteringEvent
                    {
                        TimestampUtc = DateTime.UtcNow,
                        EventSource = nameof(UriSegmentFiltering),
                        MatchKind = matchKind,
                        RemoteIpAddress = context.GetRemoteIpAddress(),
                        ObservedValue = observed
                    });
                }

                bool isAllowed = options.AllowBlacklistedRequests;

                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                    nameof(UriSegmentFiltering),
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
                    // "Blacklist but allowed" mode: you can use this to record scanner probes without blocking yet.
                    await _next(context);
                    return;
                }

                // "Blacklist and blocked" mode.
                await context.Response.WriteDefaultStatusCodeAnswerEx(options.BlockStatusCode);
                return;
            }

            if (matchKind == FilterMatchKind.Unmatched)
            {
                // Unmatched means: record optionally, then allow/block based on config.
                if (options.RecordUnmatchedRequests)
                {
                    await _filteringEventStorage.StoreAsync(new FilteringEvent
                    {
                        TimestampUtc = DateTime.UtcNow,
                        EventSource = nameof(UriSegmentFiltering),
                        MatchKind = matchKind,
                        RemoteIpAddress = context.GetRemoteIpAddress(),
                        ObservedValue = observed
                    });
                }

                bool isAllowed = options.AllowUnmatchedRequests;

                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                    nameof(UriSegmentFiltering),
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

            // Safety net: if FilterMatchKind is extended, we still keep requests flowing and log loudly.
            _logger.LogCritical(
                "ATTENTION: {MiddlewareName} received an unexpected {EnumType} value '{EnumValue}'. Your filtering logic was extended but this middleware was not updated. This should not happen.",
                () => nameof(UriSegmentFiltering),
                () => nameof(FilterMatchKind),
                () => matchKind);

            await _next(context);
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private static bool IsOnlySlashesLongerThanRoot(string path)
        {
            // Empty path is not considered suspicious here (it shouldn't normally occur).
            if (string.IsNullOrEmpty(path)) return false;

            // "/" is allowed (root).
            if (path.Length == 1) return false;

            // If every char is '/', then it is "only slashes" and longer than "/" => suspicious.
            for (int i = 0; i < path.Length; i++)
            {
                if (path[i] != '/') return false;
            }

            return true;
        }

        private static string[] GetNormalizedSegments(HttpContext context)
        {
            string path = context.Request.Path.Value ?? string.Empty;

            // Keep empty entries: "/foo//bar" should produce an empty segment between foo and bar.
            string[] raw = path.Split('/', StringSplitOptions.None);

            // Drop the leading empty entry produced by a leading slash.
            int start = (raw.Length > 0 && raw[0].Length == 0) ? 1 : 0;

            // Extract remaining tokens (may include empty strings).
            int remaining = Math.Max(0, raw.Length - start);
            if (remaining == 0)
            {
                // Extremely defensive: treat as root.
                return new[] { string.Empty };
            }

            var tokens = new string[remaining];
            Array.Copy(raw, start, tokens, 0, remaining);

            // If the path is "/" or consists only of slashes, treat it as a single empty segment ("").
            // Note: the "only slashes longer than root" case is handled earlier in InvokeAsync.
            bool anyNonEmpty = false;
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i].Length != 0)
                {
                    anyNonEmpty = true;
                    break;
                }
            }

            if (!anyNonEmpty)
            {
                return new[] { string.Empty };
            }

            // For “normal” paths, drop trailing empty segments caused purely by a trailing slash:
            // "/foo/" -> ["foo"], not ["foo", ""]
            int endExclusive = tokens.Length;
            while (endExclusive > 0 && tokens[endExclusive - 1].Length == 0)
            {
                endExclusive--;
            }

            if (endExclusive == 0)
            {
                return new[] { string.Empty };
            }

            var segments = new string[endExclusive];
            for (int i = 0; i < endExclusive; i++)
            {
                string segment = tokens[i];

                // Decode percent-escapes so "a%2Fb" is treated consistently.
                // If decoding fails we keep the raw token to avoid throwing.
                try
                {
                    segment = Uri.UnescapeDataString(segment);
                }
                catch
                {
                    // Intentionally ignore and keep raw token.
                }

                segments[i] = segment;
            }

            return segments;
        }

        private static SegmentClassificationResult ClassifySegments(string[] segments, UriSegmentFilteringOptions options)
        {
            // Defensive: if options binding yields null arrays, treat as empty lists.
            string[] whitelist = options.Whitelist ?? Array.Empty<string>();
            string[] blacklist = options.Blacklist ?? Array.Empty<string>();

            bool anyWhitelist = false;
            bool anyBlacklist = false;

            string? firstWhitelistSegment = null;
            string? firstBlacklistSegment = null;

            // Determine if any segment matches blacklist; store first match.
            for (int i = 0; i < segments.Length; i++)
            {
                if (IsBlacklistMatch(segments[i], blacklist, options.CaseSensitive))
                {
                    anyBlacklist = true;
                    firstBlacklistSegment = segments[i];
                    break;
                }
            }

            // Determine if any segment matches whitelist; store first match.
            for (int i = 0; i < segments.Length; i++)
            {
                if (IsWhitelistMatch(segments[i], whitelist, options.CaseSensitive))
                {
                    anyWhitelist = true;
                    firstWhitelistSegment = segments[i];
                    break;
                }
            }

            // Nothing matched any list.
            if (!anyWhitelist && !anyBlacklist)
            {
                return new SegmentClassificationResult(FilterMatchKind.Unmatched, observedSegment: null);
            }

            // Both matched: apply priority.
            if (anyWhitelist && anyBlacklist)
            {
                if (options.FilterPriority == FilterPriority.Whitelist)
                {
                    return new SegmentClassificationResult(FilterMatchKind.Whitelist, firstWhitelistSegment);
                }

                return new SegmentClassificationResult(FilterMatchKind.Blacklist, firstBlacklistSegment);
            }

            // Only whitelist matched.
            if (anyWhitelist)
            {
                return new SegmentClassificationResult(FilterMatchKind.Whitelist, firstWhitelistSegment);
            }

            // Only blacklist matched.
            return new SegmentClassificationResult(FilterMatchKind.Blacklist, firstBlacklistSegment);
        }

        private static bool IsWhitelistMatch(string observedSegment, string[] whitelist, bool caseSensitive)
        {
            // Reuse the standard classifier/matcher implementation by passing an empty blacklist.
            // This ensures pattern semantics match your other middlewares (wildcards, case handling, etc.).
            return FilterClassifier.Classify(
                observedSegment ?? string.Empty,
                whitelist,
                Array.Empty<string>(),
                caseSensitive,
                FilterPriority.Whitelist) == FilterMatchKind.Whitelist;
        }

        private static bool IsBlacklistMatch(string observedSegment, string[] blacklist, bool caseSensitive)
        {
            // Reuse the standard classifier/matcher implementation by passing an empty whitelist.
            return FilterClassifier.Classify(
                observedSegment ?? string.Empty,
                Array.Empty<string>(),
                blacklist,
                caseSensitive,
                FilterPriority.Blacklist) == FilterMatchKind.Blacklist;
        }

        private readonly struct SegmentClassificationResult
        {
            public SegmentClassificationResult(FilterMatchKind matchKind, string? observedSegment)
            {
                MatchKind = matchKind;
                ObservedSegment = observedSegment;
            }

            public FilterMatchKind MatchKind { get; }

            public string? ObservedSegment { get; }
        }
    }
}
