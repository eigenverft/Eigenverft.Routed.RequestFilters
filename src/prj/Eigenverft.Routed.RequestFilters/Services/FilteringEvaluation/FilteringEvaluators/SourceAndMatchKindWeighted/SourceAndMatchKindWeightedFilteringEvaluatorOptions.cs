using System;
using System.Collections.Generic;

namespace Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation.FilteringEvaluators.SourceAndMatchKindWeighted
{
    /// <summary>
    /// Options for <see cref="SourceAndMatchKindWeightedFilteringEvaluator"/>.
    /// </summary>
    /// <remarks>
    /// Reviewer note:
    /// <para>
    /// <see cref="SourceFactors"/> is seeded with code defaults. Registration uses replacement binding, so configured
    /// values fully replace defaults instead of merging with them.
    /// </para>
    /// <para>
    /// By design, a missing or explicitly empty configured dictionary retains the seeded defaults.
    /// </para>
    /// <para>
    /// An empty configured dictionary deliberately retains the code defaults.
    /// </para>
    /// <para>
    /// Example <c>appsettings.json</c> section:
    /// </para>
    /// <code>
    /// {
    ///   "SourceAndMatchKindWeightedFilteringEvaluatorOptions": {
    ///     "DefaultSourceFactor": 1,
    ///     "BlacklistWeight": 5,
    ///     "UnmatchedWeight": 1,
    ///     "Threshold": 100,
    ///     "SourceFactors": {
    ///       "HostNameFiltering": 1,
    ///       "TlsProtocolFiltering": 1,
    ///       "UserAgentFiltering": 2,
    ///       "RequestUrlFiltering": 4
    ///     }
    ///   }
    /// }
    /// </code>
    /// </remarks>
    public sealed class SourceAndMatchKindWeightedFilteringEvaluatorOptions
    {
        /// <summary>
        /// Fallback source factor used when <see cref="SourceFactors"/> does not define a factor for an event source.
        /// </summary>
        public int DefaultSourceFactor { get; set; } = 1;

        /// <summary>
        /// Per event-source multipliers applied when computing the score.
        /// </summary>
        /// <remarks>
        /// Seeded defaults are intended to be "good enough out of the box", and non-empty configuration fully replaces them.
        /// </remarks>
        public Dictionary<string, int> SourceFactors { get; set; } = new(StringComparer.Ordinal)
        {
            ["HostNameFiltering"] = 1,
            ["TlsProtocolFiltering"] = 1,
        };

        /// <summary>
        /// Weight applied to <c>Blacklist</c> matches.
        /// </summary>
        public int BlacklistWeight { get; set; } = 5;

        /// <summary>
        /// Weight applied to <c>Unmatched</c> matches.
        /// </summary>
        public int UnmatchedWeight { get; set; } = 1;

        /// <summary>
        /// Decision threshold; score values at or above this value result in a block decision.
        /// </summary>
        public int Threshold { get; set; } = 100;
    }
}
