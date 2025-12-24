using System;

using Eigenverft.Routed.RequestFilters.Options;

using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.RequestRateSmoothing
{
    /// <summary>
    /// Options for request rate smoothing using a rolling sliding window and delay-only shaping.
    /// </summary>
    /// <remarks>
    /// Reviewer note:
    /// - This middleware never rejects requests. It only adds delay based on how many requests were observed
    ///   for the same client key within the rolling window.
    /// - To avoid oscillation caused by the delay influencing the measured rate, the algorithm uses hysteresis:
    ///   it steps up immediately, but steps down slowly with a configurable hold period and step-down interval.
    /// - Configuration should replace defaults for list-like properties (no implicit merging). To support that
    ///   with the default binder behavior, list defaults are wrapped in <see cref="OptionsConfigOverridesDefaultsList{T}"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// {
    ///   "RequestRateSmoothingOptions": {
    ///     "WindowSize": "00:01:00",
    ///     "BucketSize": "00:00:01",
    ///     "HoldLevelAfterIncreaseFor": "00:00:15",
    ///     "StepDownEvery": "00:00:03",
    ///     "StepDownHysteresisRatio": 0.8,
    ///     "ClampDelayToAtMostMilliseconds": 0,
    ///     "ForgetClientAfterNoRequestsFor": "00:10:00",
    ///     "RunStaleClientCleanupEveryNRequests": 16384,
    ///     "MissingClientKeyBehavior": "SharedBucket",
    ///     "LogLevelClientObservedBelowFirstStep": "Debug",
    ///     "LogLevelClientSmoothingLevelChanged": "Information",
    ///     "LogLevelClientEnteredMaxSmoothingLevel": "Warning",
    ///     "LogLevelClientSmoothingObservation": "Debug",
    ///     "Steps": [
    ///       { "ExceedsRequestsInWindow": 120, "DelayMilliseconds": 5 },
    ///       { "ExceedsRequestsInWindow": 180, "DelayMilliseconds": 15 },
    ///       { "ExceedsRequestsInWindow": 240, "DelayMilliseconds": 25 },
    ///       { "ExceedsRequestsInWindow": 300, "DelayMilliseconds": 50 },
    ///       { "ExceedsRequestsInWindow": 360, "DelayMilliseconds": 75 },
    ///       { "ExceedsRequestsInWindow": 420, "DelayMilliseconds": 125 },
    ///       { "ExceedsRequestsInWindow": 480, "DelayMilliseconds": 250 },
    ///       { "ExceedsRequestsInWindow": 540, "DelayMilliseconds": 333 },
    ///       { "ExceedsRequestsInWindow": 600, "DelayMilliseconds": 500 }
    ///     ]
    ///   }
    /// }
    /// </code>
    /// </example>
public sealed class RequestRateSmoothingOptions
    {
        /// <summary>
        /// Gets or sets the size of the rolling window used to count requests per client.
        /// </summary>
        public TimeSpan WindowSize { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Gets or sets the bucket size used to approximate the rolling window.
        /// </summary>
        /// <remarks>
        /// Example: with a 60 second window and 1 second buckets, the state holds 60 buckets.
        /// Smaller buckets increase precision but also increase per-client memory.
        /// </remarks>
        public TimeSpan BucketSize { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Gets or sets the delay steps applied when the request count in the current window exceeds a threshold.
        /// </summary>
        /// <remarks>
        /// Reviewer note:
        /// This property is backed by <see cref="OptionsConfigOverridesDefaultsList{T}"/> to enforce
        /// "configuration replaces defaults" semantics during binding:
        /// - If configuration provides any <see cref="RequestRateSmoothingStep"/>, the seeded defaults are cleared once
        ///   and the configured items fully replace them.
        /// - If configuration does not provide <c>Steps</c>, the seeded defaults remain active.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<RequestRateSmoothingStep> Steps { get; set; } = new(
            new() { ExceedsRequestsInWindow = 120, DelayMilliseconds = 5 },
            new() { ExceedsRequestsInWindow = 180, DelayMilliseconds = 15 },
            new() { ExceedsRequestsInWindow = 240, DelayMilliseconds = 25 },
            new() { ExceedsRequestsInWindow = 300, DelayMilliseconds = 50 },
            new() { ExceedsRequestsInWindow = 360, DelayMilliseconds = 75 },
            new() { ExceedsRequestsInWindow = 420, DelayMilliseconds = 125 },
            new() { ExceedsRequestsInWindow = 480, DelayMilliseconds = 250 },
            new() { ExceedsRequestsInWindow = 540, DelayMilliseconds = 333 },
            new() { ExceedsRequestsInWindow = 600, DelayMilliseconds = 500 }
        );

        /// <summary>
        /// Gets or sets how long a newly increased level is held before any step-down is permitted.
        /// </summary>
        /// <remarks>
        /// This is the primary mechanism that prevents "falling out of the window" because the middleware itself adds delay.
        /// </remarks>
        public TimeSpan HoldLevelAfterIncreaseFor { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets how often the middleware may decrease the active level by at most one step.
        /// </summary>
        /// <remarks>
        /// Example: if set to 5 seconds, then even if the client becomes quiet, the delay level ramps down gradually.
        /// </remarks>
        public TimeSpan StepDownEvery { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets or sets the hysteresis ratio used for stepping down.
        /// </summary>
        /// <remarks>
        /// Reviewer note:
        /// - Up-threshold is <see cref="RequestRateSmoothingStep.ExceedsRequestsInWindow"/>.
        /// - Down-threshold is UpThreshold multiplied by this ratio.
        /// - Typical values are 0.7 to 0.9.
        /// </remarks>
        public double StepDownHysteresisRatio { get; set; } = 0.8;

        /// <summary>
        /// Gets or sets an optional safety clamp for the delay applied to a single request (0 disables clamping).
        /// </summary>
        public int ClampDelayToAtMostMilliseconds { get; set; } = 0;

        /// <summary>
        /// Gets or sets how long a client may be inactive before its stored smoothing state is forgotten.
        /// </summary>
        public TimeSpan ForgetClientAfterNoRequestsFor { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Gets or sets how often the middleware scans for inactive clients and removes them (every N incoming requests).
        /// </summary>
        public int RunStaleClientCleanupEveryNRequests { get; set; } = 16384;

        /// <summary>
        /// Gets or sets the behavior when the client key cannot be resolved.
        /// </summary>
        public MissingClientKeyBehavior MissingClientKeyBehavior { get; set; } = MissingClientKeyBehavior.SharedBucket;

        /// <summary>
        /// Gets or sets the log level used when a client is first observed and remains below the first smoothing threshold (level 0).
        /// </summary>
        /// <remarks>
        /// Reviewer note:
        /// This is intentionally "first seen only" to avoid log noise.
        /// </remarks>
        public LogLevel LogLevelClientObservedBelowFirstStep { get; set; } = LogLevel.Debug;

        /// <summary>
        /// Gets or sets the log level used when a client changes smoothing level (including 0 to 1 and 1 to 0).
        /// </summary>
        public LogLevel LogLevelClientSmoothingLevelChanged { get; set; } = LogLevel.Information;

        /// <summary>
        /// Gets or sets the log level used when a client enters the last (maximum) smoothing level.
        /// </summary>
        public LogLevel LogLevelClientEnteredMaxSmoothingLevel { get; set; } = LogLevel.Warning;

        /// <summary>
        /// Gets or sets the log level used to emit a per-request observation line containing
        /// the current window count and an inexpensive request-rate approximation.
        /// </summary>
        /// <remarks>
        /// Reviewer note:
        /// - This log can be noisy because it may emit on every request.
        /// - The reported rate is an approximation: <c>TotalInWindow / WindowSeconds</c>.
        /// - Set to <see cref="LogLevel.None"/> to disable.
        /// </remarks>
        public LogLevel LogLevelClientSmoothingObservation { get; set; } = LogLevel.Debug;
    }

    /// <summary>
    /// A single smoothing step: when requests exceed a threshold in the rolling window, apply a delay.
    /// </summary>
    public sealed class RequestRateSmoothingStep
    {
        /// <summary>
        /// Gets or sets the request-count threshold (exclusive). When the count is greater than this value, the step is active.
        /// </summary>
        public int ExceedsRequestsInWindow { get; set; }

        /// <summary>
        /// Gets or sets the delay to apply when the step is active.
        /// </summary>
        public int DelayMilliseconds { get; set; }
    }

    /// <summary>
    /// Defines how the middleware behaves when no client key can be derived for a request.
    /// </summary>
    public enum MissingClientKeyBehavior
    {
        /// <summary>
        /// Bypasses smoothing when the client key is missing.
        /// </summary>
        Bypass = 0,

        /// <summary>
        /// Uses a single shared bucket for all requests with missing client key.
        /// </summary>
        SharedBucket = 1
    }
}
