using System;

using Eigenverft.WebLib.Middleware.Primitives.Features;

using Microsoft.AspNetCore.Http;

namespace Eigenverft.Routed.RequestFilters.Middleware.FilteringEvaluationGate
{
    /// <summary>
    /// Provides typed access to the per-request evaluator decision feature.
    /// </summary>
    /// <remarks>
    /// The value is request-scoped and intended for downstream middleware (for example, redirect or response shaping)
    /// that needs to know whether the evaluator would have blocked the request, independent of whether it was actually
    /// blocked (see log-only rollout via <c>AllowBlockedRequests</c>).
    /// </remarks>
    public static class FilteringEvaluationGateHttpContextMarkers
    {
        /// <summary>
        /// Sets a per-request marker indicating whether the evaluator would block the current request.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <param name="evaluatorWouldBlock"><c>true</c> if the evaluator would block; otherwise <c>false</c>.</param>
        public static void SetMarkedAsBlockedByEvaluator(this HttpContext context, bool evaluatorWouldBlock)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.SetFeature(new FilteringEvaluationFeature(evaluatorWouldBlock));
        }

        /// <summary>
        /// Gets a per-request marker indicating whether the evaluator would block the current request.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>
        /// <c>true</c> if the marker was set to <c>true</c>; otherwise <c>false</c>.
        /// If the marker was not set, this returns <c>false</c>.
        /// </returns>
        public static bool GetMarkedAsBlockedByEvaluator(this HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.GetFeature<FilteringEvaluationFeature>()?.EvaluatorWouldBlock ?? false;
        }

        /// <summary>
        /// Tries to read the per-request marker indicating whether the evaluator would block the current request.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <param name="evaluatorWouldBlock">The stored value if present and of type <see cref="bool"/>.</param>
        /// <returns><c>true</c> if the marker exists; otherwise <c>false</c>.</returns>
        public static bool TryGetEvaluatorWouldBlock(this HttpContext context, out bool evaluatorWouldBlock)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.TryGetFeature<FilteringEvaluationFeature>(out var feature))
            {
                evaluatorWouldBlock = feature.EvaluatorWouldBlock;
                return true;
            }

            evaluatorWouldBlock = false;
            return false;
        }

        private sealed class FilteringEvaluationFeature
        {
            internal FilteringEvaluationFeature(bool evaluatorWouldBlock)
            {
                EvaluatorWouldBlock = evaluatorWouldBlock;
            }

            internal bool EvaluatorWouldBlock { get; }
        }
    }
}
