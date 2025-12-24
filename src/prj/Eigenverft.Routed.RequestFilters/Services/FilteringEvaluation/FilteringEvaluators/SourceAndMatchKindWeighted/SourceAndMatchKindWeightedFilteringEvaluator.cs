using System;
using System.Globalization;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;

using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation.FilteringEvaluators.SourceAndMatchKindWeighted
{
    /// <summary>
    /// Evaluator that derives a decision from per-source and per-match-kind aggregates using configurable weights.
    /// </summary>
    /// <remarks>
    /// Reviewer note:
    /// The score is computed as:
    /// <c>score = Σ(count * sourceFactor * matchKindWeight)</c>.
    /// This evaluator uses <see cref="IFilteringEventStorage.GetByEventSourceAndMatchKind(string)"/> to keep match-kind information per event source.
    /// All weights and factors are read from <see cref="SourceAndMatchKindWeightedFilteringEvaluatorOptions"/> via <see cref="IOptionsMonitor{TOptions}"/>
    /// to support live reload.
    /// </remarks>
    public sealed class SourceAndMatchKindWeightedFilteringEvaluator : IFilteringEvaluationService
    {
        private readonly IFilteringEventStorage _storage;
        private readonly IDeferredLogger<SourceAndMatchKindWeightedFilteringEvaluator> _logger;
        private readonly IOptionsMonitor<SourceAndMatchKindWeightedFilteringEvaluatorOptions> _optionsMonitor;

        /// <summary>
        /// Creates a new evaluator.
        /// </summary>
        /// <param name="storage">Event storage backend.</param>
        /// <param name="logger">Deferred logger.</param>
        /// <param name="optionsMonitor">Options monitor for live-updating configuration.</param>
        public SourceAndMatchKindWeightedFilteringEvaluator(
            IFilteringEventStorage storage,
            IDeferredLogger<SourceAndMatchKindWeightedFilteringEvaluator> logger,
            IOptionsMonitor<SourceAndMatchKindWeightedFilteringEvaluatorOptions> optionsMonitor)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));

            _optionsMonitor.OnChange(_ => _logger.LogDebug("Configuration for {Service} updated.", () => nameof(SourceAndMatchKindWeightedFilteringEvaluator)));
        }

        /// <summary>
        /// Evaluates a remote IP address and returns a filtering decision.
        /// </summary>
        /// <param name="remoteIpAddress">Remote IP address.</param>
        /// <returns>The evaluation result.</returns>
        public FilteringEvaluationResult Evaluate(string remoteIpAddress)
        {
            if (remoteIpAddress is null) throw new ArgumentNullException(nameof(remoteIpAddress));

            var options = _optionsMonitor.CurrentValue;
            var rows = _storage.GetByEventSourceAndMatchKind(remoteIpAddress);

            long score = 0;

            // Keep one top contributor without sorting (cheap and useful).
            long topContribution = 0;
            string topSource = string.Empty;
            FilterMatchKind topKind = default;
            long topCount = 0;

            int defaultSourceFactor = options.DefaultSourceFactor;
            int blacklistWeight = options.BlacklistWeight;
            int unmatchedWeight = options.UnmatchedWeight;

            // Defensive: allow SourceFactors to be present but null if someone manually sets it in code.
            var sourceFactors = options.SourceFactors;

            foreach (var row in rows)
            {
                int matchKindWeight = GetMatchKindWeight(row.MatchKind, blacklistWeight, unmatchedWeight);
                if (matchKindWeight == 0)
                {
                    continue;
                }

                int sourceFactor = defaultSourceFactor;

                if (row.EventSource.Length != 0 &&
                    sourceFactors is not null &&
                    sourceFactors.TryGetValue(row.EventSource, out var configuredFactor))
                {
                    sourceFactor = configuredFactor;
                }

                long contribution = row.Count * (long)sourceFactor * matchKindWeight;
                score += contribution;

                if (contribution > topContribution)
                {
                    topContribution = contribution;
                    topSource = row.EventSource;
                    topKind = row.MatchKind;
                    topCount = row.Count;
                }
            }

            int value = score > int.MaxValue ? int.MaxValue : (int)score;
            bool block = value >= options.Threshold;

            string reason = string.Format(
                CultureInfo.InvariantCulture,
                "value={0} from Σ(count*sourceFactor*matchKindWeight); buckets={1}; top={2}:{3} count={4} contrib={5}; threshold={6}.",
                value,
                rows.Count,
                topSource,
                topKind,
                topCount,
                topContribution,
                options.Threshold);

            return new FilteringEvaluationResult
            {
                Decision = block ? FilteringDecision.Block : FilteringDecision.Allow,
                EvaluationReason = reason
            };
        }

        /// <summary>
        /// Maps a match kind to its configured weight.
        /// </summary>
        /// <param name="matchKind">Match kind.</param>
        /// <param name="blacklistWeight">Configured blacklist weight.</param>
        /// <param name="unmatchedWeight">Configured unmatched weight.</param>
        /// <returns>Weight for the match kind, or 0 for kinds that do not contribute.</returns>
        private static int GetMatchKindWeight(FilterMatchKind matchKind, int blacklistWeight, int unmatchedWeight)
        {
            return matchKind switch
            {
                FilterMatchKind.Blacklist => blacklistWeight,
                FilterMatchKind.Unmatched => unmatchedWeight,
                _ => 0
            };
        }
    }
}