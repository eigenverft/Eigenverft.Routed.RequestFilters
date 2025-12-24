using System;
using System.Globalization;

using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;

namespace Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation.FilteringEvaluators.SimpleFilteringScoreEvaluator
{
    /// <summary>
    /// Simple evaluator that derives a decision from stored blacklist and unmatched counts.
    /// </summary>
    /// <remarks>
    /// The evaluation uses a weighted sum: <c>value = (blacklistCount * BlacklistWeight) + (unmatchedCount * UnmatchedWeight)</c>.
    /// The decision is <see cref="FilteringDecision.Block"/> when the computed value is greater than or equal to <c>Threshold</c>.
    /// </remarks>
    public sealed class SimpleFilteringScoreFilteringEvaluator : IFilteringEvaluationService
    {
        private readonly IFilteringEventStorage _storage;

        private const int BlacklistWeight = 5;
        private const int UnmatchedWeight = 1;
        private const int Threshold = 20;

        public SimpleFilteringScoreFilteringEvaluator(IFilteringEventStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public FilteringEvaluationResult Evaluate(string remoteIpAddress)
        {
            int blacklistCount = _storage.GetBlacklistCount(remoteIpAddress);
            int unmatchedCount = _storage.GetUnmatchedCount(remoteIpAddress);

            int value = (blacklistCount * BlacklistWeight) + (unmatchedCount * UnmatchedWeight);
            bool block = value >= Threshold;

            string reason = string.Format(CultureInfo.InvariantCulture, "value={0} from blacklistCount={1}*{2} + unmatchedCount={3}*{4}; threshold={5}.", value, blacklistCount, BlacklistWeight, unmatchedCount, UnmatchedWeight, Threshold);

            return new FilteringEvaluationResult { Decision = block ? FilteringDecision.Block : FilteringDecision.Allow, EvaluationReason = reason };
        }
    }
}