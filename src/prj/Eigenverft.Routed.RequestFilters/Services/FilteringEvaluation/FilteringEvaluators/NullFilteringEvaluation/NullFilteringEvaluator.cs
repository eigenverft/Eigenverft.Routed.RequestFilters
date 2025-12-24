using System;

namespace Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation.FilteringEvaluators.NullFilteringEvaluation
{
    /// <summary>
    /// Null evaluator that performs no scoring and never blocks.
    /// </summary>
    /// <remarks>
    /// This implementation intentionally returns a neutral "allow" decision for every request.
    /// It is meant for scenarios where filtering evaluation is temporarily disabled, not configured,
    /// or needs to be bypassed without changing middleware/pipeline wiring.
    ///
    /// The evaluator still returns a deterministic <see cref="FilteringEvaluationResult"/> with a clear
    /// <see cref="FilteringEvaluationResult.EvaluationReason"/> to aid diagnostics and avoid ambiguity.
    /// </remarks>
    public sealed class NullFilteringEvaluator : IFilteringEvaluationService
    {
        /// <summary>
        /// Evaluates the request in a no-op manner.
        /// </summary>
        /// <param name="remoteIpAddress">The remote IP address for which an evaluation would normally be performed.</param>
        /// <returns>
        /// A <see cref="FilteringEvaluationResult"/> that always allows the request and explains that evaluation is disabled.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="remoteIpAddress"/> is null.</exception>
        /// <example>
        /// <code>
        /// IFilteringEvaluationService evaluator = new NullFilteringEvaluationService();
        /// FilteringEvaluationResult result = evaluator.Evaluate("203.0.113.10");
        /// // result.Decision == FilteringDecision.Allow
        /// </code>
        /// </example>
        public FilteringEvaluationResult Evaluate(string remoteIpAddress)
        {
            ArgumentNullException.ThrowIfNull(remoteIpAddress);

            return new FilteringEvaluationResult
            {
                Decision = FilteringDecision.Allow,
                EvaluationReason = "Null evaluator: filtering evaluation is disabled; no scoring was performed and the request was allowed."
            };
        }
    }
}
