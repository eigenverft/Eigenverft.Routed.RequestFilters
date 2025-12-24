using System;
using System.Collections.Generic;

using Eigenverft.Routed.RequestFilters.Options;

namespace Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation.FilteringEvaluators.SourceAndMatchKindWeighted
{
    /// <summary>
    /// Options for <see cref="SourceAndMatchKindWeightedFilteringEvaluator"/>.
    /// </summary>
    /// <remarks>
    /// Reviewer note:
    /// <para>
    /// <see cref="SourceFactors"/> is seeded with code defaults, but uses "configuration overrides defaults" semantics:
    /// the first configuration write clears seeded defaults once, so configured values fully replace defaults (no merging).
    /// </para>
    /// <para>
    /// By design, a missing configuration section (or a present-but-empty section with no children) typically results in no binder writes,
    /// so the seeded defaults remain in effect.
    /// </para>
    /// <para>
    /// If you ever need an explicit "empty means empty" outcome, opt into that intentionally (for example via a separate flag)
    /// and call <see cref="OptionsConfigOverridesDefaultsDictionary{TKey, TValue}.Clear"/> in post-configure.
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
        /// Reviewer note:
        /// Seeded defaults are intended to be "good enough out of the box", and configuration can fully replace them.
        /// Avoid using collection-initializer syntax on this property type, because it calls <see cref="IDictionary{TKey, TValue}.Add(TKey, TValue)"/>
        /// and would be treated as an override write.
        /// </remarks>
        public OptionsConfigOverridesDefaultsDictionary<string, int> SourceFactors { get; set; }
            = new(
                dictionary: new Dictionary<string, int>
                {
                    ["HostNameFiltering"] = 1,
                    ["TlsProtocolFiltering"] = 1,
                },
                comparer: StringComparer.Ordinal);

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
