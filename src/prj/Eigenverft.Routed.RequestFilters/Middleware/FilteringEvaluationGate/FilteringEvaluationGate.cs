using System;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.GenericExtensions.HttpResponseExtensions;
using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.FilteringEvaluationGate
{
    /// <summary>
    /// Middleware that gates requests based on the decision produced by an <see cref="IFilteringEvaluationService"/>.
    /// </summary>
    /// <remarks>
    /// This middleware evaluates the current request (by remote IP address) and either:
    /// <list type="bullet">
    /// <item>
    /// <description>blocks the request by returning <see cref="FilteringEvaluationGateOptions.BlockStatusCode"/>, or</description>
    /// </item>
    /// <item>
    /// <description>allows the request to continue to the next middleware in the pipeline.</description>
    /// </item>
    /// </list>
    /// <para>
    /// The <see cref="FilteringEvaluationGateOptions.AllowBlockedRequests"/> option enables a "log-only rollout" mode:
    /// even if the evaluator would block, the request is still allowed to proceed.
    /// </para>
    /// <para>
    /// When enabled via <see cref="FilteringEvaluationGateOptions.EmitMarkedAsBlockedByEvaluator"/>, and when the request
    /// is allowed to proceed while the evaluator would have blocked it, the middleware sets a request marker in
    /// <see cref="HttpContext.Items"/> using <see cref="FilteringEvaluationGateHttpContextMarkers.SetMarkedAsBlockedByEvaluator(HttpContext,bool)"/>.
    /// This enables downstream middleware (for example, proxy routing, redirects, or response shaping) to react without
    /// relying on re-running the evaluator.
    /// </para>
    /// </remarks>
    public class FilteringEvaluationGate
    {
        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<FilteringEvaluationGate> _logger;
        private readonly IOptionsMonitor<FilteringEvaluationGateOptions> _optionsMonitor;
        private readonly IFilteringEvaluationService _evaluationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="FilteringEvaluationGate"/> class.
        /// </summary>
        /// <param name="nextMiddleware">The next middleware in the pipeline.</param>
        /// <param name="logger">The deferred logger instance.</param>
        /// <param name="optionsMonitor">The options monitor for <see cref="FilteringEvaluationGateOptions"/>.</param>
        /// <param name="evaluationService">The evaluation service used to decide whether a request should be blocked.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="nextMiddleware"/>, <paramref name="logger"/>, <paramref name="optionsMonitor"/>,
        /// or <paramref name="evaluationService"/> is <see langword="null"/>.
        /// </exception>
        public FilteringEvaluationGate(
            RequestDelegate nextMiddleware,
            IDeferredLogger<FilteringEvaluationGate> logger,
            IOptionsMonitor<FilteringEvaluationGateOptions> optionsMonitor,
            IFilteringEvaluationService evaluationService)
        {
            _next = nextMiddleware ?? throw new ArgumentNullException(nameof(nextMiddleware));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _evaluationService = evaluationService ?? throw new ArgumentNullException(nameof(evaluationService));

            _optionsMonitor.OnChange(_ => _logger.LogDebug("Configuration for {MiddlewareName} updated.", () => nameof(FilteringEvaluationGate)));
        }

        /// <summary>
        /// Evaluates the current request and either forwards it or blocks it according to the configured gate behavior.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="context"/> is <see langword="null"/>.</exception>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            FilteringEvaluationGateOptions options = _optionsMonitor.CurrentValue;

            string observed = context.GetRemoteIpAddress();
            string trace = context.TraceIdentifier;

            FilteringEvaluationResult evaluation = _evaluationService.Evaluate(observed);

            bool shouldBlock = evaluation.ShouldBlock;

            // "Pass-through" mode: evaluator would block, but we allow it through.
            bool isAllowed = !shouldBlock || options.AllowBlockedRequests;

            // Track whether THIS middleware instance emitted the marker (for stable logging).
            bool markedAsBlockedByEvaluator = false;

            // Only set the marker when it matters:
            // - evaluator indicates "would block"
            // - request is actually allowed to continue (log-only rollout / pass-through)
            // - option enabled
            if (isAllowed && shouldBlock && options.EmitMarkedAsBlockedByEvaluator)
            {
                context.SetMarkedAsBlockedByEvaluator(true);
                markedAsBlockedByEvaluator = true;
            }

            LogLevel level = shouldBlock ? options.LogLevelBlocked : options.LogLevelAllowed;
            LogDecision(level, trace, isAllowed, observed, evaluation.EvaluationReason, markedAsBlockedByEvaluator);

            // Short-circuit when blocked (unless allow-through is enabled).
            if (!isAllowed)
            {
                await context.Response.WriteDefaultStatusCodeAnswerEx(options.BlockStatusCode);
                return;
            }

            await _next(context);
        }

        /// <summary>
        /// Writes a single, stable state log line for this middleware.
        /// </summary>
        /// <param name="level">The resolved log level.</param>
        /// <param name="traceIdentifier">The current request trace identifier.</param>
        /// <param name="isAllowed">Whether the request is allowed to proceed.</param>
        /// <param name="observedValue">The observed value (remote ip address).</param>
        /// <param name="evaluationReason">Optional evaluation reason.</param>
        /// <param name="markedAsBlockedByEvaluator">
        /// Whether this middleware instance marked the request as "evaluator would block" for downstream processing.
        /// </param>
        private void LogDecision(
            LogLevel level,
            string traceIdentifier,
            bool isAllowed,
            string observedValue,
            string? evaluationReason,
            bool markedAsBlockedByEvaluator)
        {
            if (level == LogLevel.None || !_logger.IsEnabled(level)) return;

            const string match = "Evaluator";
            string decision = isAllowed ? "Allowed" : "Blocked";

            _logger.Log(
                level,
                "{Middleware} match={Match} decision={Decision} observed={Observed} marked={Marked} reason={Reason} trace={Trace}",
                () => nameof(FilteringEvaluationGate),
                () => match,
                () => decision,
                () => observedValue,
                () => markedAsBlockedByEvaluator,
                () => evaluationReason ?? string.Empty,
                () => traceIdentifier

            );
        }
    }
}
