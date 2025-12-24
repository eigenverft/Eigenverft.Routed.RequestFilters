using System;

using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.Abstractions
{
    /// <summary>
    /// Represents a single log decision with a resolved level and a structured message template + deferred arguments.
    /// </summary>
    /// <param name="Level">The resolved log level for this decision.</param>
    /// <param name="MessageTemplate">The message template passed to the logger.</param>
    /// <param name="Args">Deferred arguments corresponding to the message template.</param>
    public readonly record struct FilterDecisionLogEntry(LogLevel Level, string MessageTemplate, Func<object>[] Args);

    /// <summary>
    /// Builds log decision entries for filter-like middlewares.
    /// </summary>
    public static class FilterDecisionLogBuilder
    {
        /// <summary>
        /// Builds a single-line log entry for filter-like middlewares, resolving the log level from the match kind
        /// and emitting a stable message shape: middleware, match, decision, observed, and whether the event was logged for the evaluator.
        /// </summary>
        /// <param name="middlewareName">The middleware name, typically <c>nameof(TheMiddleware)</c>.</param>
        /// <param name="matchKind">The match kind produced by the classifier.</param>
        /// <param name="isAllowed">Whether the request is allowed to proceed.</param>
        /// <param name="observedValue">The observed value (e.g. remote ip, host, protocol, path).</param>
        /// <param name="loggedForEvaluator">Whether this decision was logged for later evaluation (i.e. recorded into event storage).</param>
        /// <param name="logLevelWhitelist">Log level for whitelist matches.</param>
        /// <param name="logLevelBlacklist">Log level for blacklist matches.</param>
        /// <param name="logLevelUnmatched">Log level for unmatched results.</param>
        /// <returns>
        /// A <see cref="FilterDecisionLogEntry"/> containing the resolved log level and template+deferred arguments.
        /// If the resolved level is <see cref="LogLevel.None"/>, the returned entry has an empty template and no arguments.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="middlewareName"/> or <paramref name="observedValue"/> is null.</exception>
        public static FilterDecisionLogEntry Create(
            string middlewareName,
            string traceIdentifier,
            FilterMatchKind matchKind,
            bool isAllowed,
            string observedValue,
            bool loggedForEvaluator,
            LogLevel logLevelWhitelist,
            LogLevel logLevelBlacklist,
            LogLevel logLevelUnmatched)
        {
            if (middlewareName == null) throw new ArgumentNullException(nameof(middlewareName));
            if (observedValue == null) throw new ArgumentNullException(nameof(observedValue));

            LogLevel level;
            string matchKindText;

            if (matchKind == FilterMatchKind.Whitelist)
            {
                level = logLevelWhitelist;
                matchKindText = nameof(FilterMatchKind.Whitelist);
            }
            else if (matchKind == FilterMatchKind.Blacklist)
            {
                level = logLevelBlacklist;
                matchKindText = nameof(FilterMatchKind.Blacklist);
            }
            else if (matchKind == FilterMatchKind.Unmatched)
            {
                level = logLevelUnmatched;
                matchKindText = nameof(FilterMatchKind.Unmatched);
            }
            else
            {
                return new FilterDecisionLogEntry(LogLevel.None, string.Empty, Array.Empty<Func<object>>());
            }

            if (level == LogLevel.None)
            {
                return new FilterDecisionLogEntry(LogLevel.None, string.Empty, Array.Empty<Func<object>>());
            }

            string decision = isAllowed ? "Allowed" : "Blocked";

            return new FilterDecisionLogEntry(
                level,
                "{Middleware} match={Match} decision={Decision} observed={Observed} loggedForEvaluator={LoggedForEvaluator} trace={Trace}"
,
                new Func<object>[]
                {
                    () => middlewareName,
                    () => matchKindText,
                    () => decision,
                    () => observedValue,
                    () => loggedForEvaluator,
                    () => traceIdentifier,
                });
        }
    }
}
