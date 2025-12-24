using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.FilteringEvaluationGate
{
    /// <summary>
    /// Provides configuration options for the evaluation gate middleware.
    /// </summary>
    /// <remarks>
    /// Bindable from configuration section <c>FilteringEvaluationGateOptions</c>. If the section is missing,
    /// property initializers act as defaults.
    /// <para>Example configuration snippets:</para>
    /// <para><b>1) Default behavior (gate enforces blocks, minimal logging, no context marker)</b></para>
    /// <code>
    /// "FilteringEvaluationGateOptions": {
    ///   "BlockStatusCode": 400,
    ///   "AllowBlockedRequests": false,
    ///   "LogLevelBlocked": "Debug",
    ///   "LogLevelAllowed": "Debug",
    ///   "EmitMarkedAsBlockedByEvaluator": false
    /// }
    /// </code>
    /// <para><b>2) Log-only rollout + emit marker when evaluator would block (for redirects / downstream handling)</b></para>
    /// <code>
    /// "FilteringEvaluationGateOptions": {
    ///   "BlockStatusCode": 400,
    ///   "AllowBlockedRequests": true,
    ///   "LogLevelBlocked": "Debug",
    ///   "LogLevelAllowed": "Debug",
    ///   "EmitMarkedAsBlockedByEvaluator": true
    /// }
    /// </code>
    /// </remarks>
    public sealed class FilteringEvaluationGateOptions
    {
        /// <summary>
        /// Gets or sets the http status code that is used when a request is blocked by the evaluator.
        /// </summary>
        public int BlockStatusCode { get; set; } = StatusCodes.Status400BadRequest;

        /// <summary>
        /// Gets or sets a value indicating whether requests that the evaluator would block should still be allowed to pass through.
        /// </summary>
        /// <remarks>
        /// This can be used as a log-only rollout mode while tuning the evaluator.
        /// </remarks>
        public bool AllowBlockedRequests { get; set; } = false;

        /// <summary>
        /// Gets or sets the log level used when the evaluator blocks a request.
        /// </summary>
        /// <remarks>
        /// Use <see cref="LogLevel.None"/> to disable logging for blocked requests.
        /// </remarks>
        public LogLevel LogLevelBlocked { get; set; } = LogLevel.Debug;

        /// <summary>
        /// Gets or sets the log level used when the evaluator allows a request.
        /// </summary>
        /// <remarks>
        /// Use <see cref="LogLevel.None"/> to disable logging for allowed requests.
        /// </remarks>
        public LogLevel LogLevelAllowed { get; set; } = LogLevel.None;

        /// <summary>
        /// Gets or sets a value indicating whether the middleware should write the
        /// <c>MarkedAsBlockedByEvaluator</c> request marker to <see cref="HttpContext.Items"/> when the evaluator
        /// indicates a block decision.
        /// </summary>
        /// <remarks>
        /// When enabled and the evaluator indicates a block, the middleware sets the marker via
        /// <see cref="FilteringEvaluationGateHttpContextMarkers.SetMarkedAsBlockedByEvaluator(HttpContext,bool)"/>.
        /// The marker is written regardless of <see cref="AllowBlockedRequests"/> so downstream middleware can
        /// react consistently in log-only rollout mode.
        /// </remarks>
        public bool EmitMarkedAsBlockedByEvaluator { get; set; } = false;
    }
}
