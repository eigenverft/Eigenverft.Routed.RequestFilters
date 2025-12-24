namespace Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation
{
    /// <summary>
    /// Defines the decision produced by an evaluation.
    /// </summary>
    public enum FilteringDecision
    {
        /// <summary>
        /// The request should continue through the pipeline.
        /// </summary>
        Allow,

        /// <summary>
        /// The request should be blocked.
        /// </summary>
        Block
    }

    /// <summary>
    /// Represents the result of evaluating a client based on filtering events.
    /// </summary>
    public sealed class FilteringEvaluationResult
    {
        /// <summary>
        /// Gets or sets the decision produced by the evaluator.
        /// </summary>
        public FilteringDecision Decision { get; set; } = FilteringDecision.Allow;

        /// <summary>
        /// Gets a value indicating whether the evaluator decided to block.
        /// </summary>
        public bool ShouldBlock => Decision == FilteringDecision.Block;

        /// <summary>
        /// Gets or sets a human readable explanation describing why the evaluator produced the decision.
        /// </summary>
        /// <remarks>
        /// This value is intended for diagnostics and logging.
        /// </remarks>
        public string EvaluationReason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Evaluates stored filtering events and produces a decision for a client.
    /// </summary>
    public interface IFilteringEvaluationService
    {
        /// <summary>
        /// Evaluates the specified remote ip address and returns an evaluation result.
        /// </summary>
        /// <param name="remoteIpAddress">The normalized remote ip address.</param>
        /// <returns>The evaluation result.</returns>
        FilteringEvaluationResult Evaluate(string remoteIpAddress);
    }
}
