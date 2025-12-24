using System;

using Eigenverft.Routed.RequestFilters.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.RequestDelayThrottling
{
    /// <summary>
    /// Options for request delay throttling.
    /// </summary>
    /// <remarks>
    /// This middleware does not block requests; it delays them based on a per-client request count within a time window.
    /// Arrays are replaced by options binding when configuration provides a value.
    /// </remarks>
    public sealed class RequestDelayThrottlingOptions
    {
        /// <summary>
        /// Gets or sets the time window used for counting requests per client.
        /// </summary>
        /// <remarks>
        /// Example: When set to 10 seconds, <see cref="DelaySteps"/> thresholds apply to the number of requests seen in the last 10 seconds.
        /// </remarks>
        public TimeSpan CountRequestsWithin { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the delay steps applied when the request count within the current window exceeds a threshold.
        /// </summary>
        /// <remarks>
        /// The selected delay is the one with the highest <see cref="RequestDelayStep.Exceeds"/> that is still exceeded.
        /// </remarks>
        /// <example>
        /// <code>
        /// "RequestDelayThrottlingOptions": {
        ///   "CountRequestsWithin": "00:00:30",
        ///   "DelaySteps": [
        ///     { "Exceeds": 100, "DelayMilliseconds": 50 },
        ///     { "Exceeds": 200, "DelayMilliseconds": 100 },
        ///     { "Exceeds": 400, "DelayMilliseconds": 250 }
        ///   ]
        /// }
        /// </code>
        /// </example>
        public OptionsConfigOverridesDefaultsList<RequestDelayStep> DelaySteps { get; set; } = new[]
        {
            new RequestDelayStep { Exceeds = 100, DelayMilliseconds = 50 },
            new RequestDelayStep { Exceeds = 200, DelayMilliseconds = 100 },
            new RequestDelayStep { Exceeds = 400, DelayMilliseconds = 250 },
            new RequestDelayStep { Exceeds = 800, DelayMilliseconds = 500 },
            new RequestDelayStep { Exceeds = 1600, DelayMilliseconds = 1000 },
        };

        /// <summary>
        /// Gets or sets an optional safety clamp for the delay applied to a single request (0 disables clamping).
        /// </summary>
        /// <remarks>
        /// Not required when using absolute step delays, but protects against accidental extreme configuration values.
        /// </remarks>
        public int ClampDelayToAtMostMilliseconds { get; set; } = 0;

        /// <summary>
        /// Gets or sets how long a client may be inactive before its stored throttling state is forgotten.
        /// </summary>
        /// <remarks>
        /// This removes stored per-client counters after inactivity, preventing memory growth from one-off clients.
        /// </remarks>
        public TimeSpan ForgetClientAfterNoRequestsFor { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Gets or sets how often the middleware scans for inactive clients and removes them (every N incoming requests).
        /// </summary>
        /// <remarks>
        /// Default is intentionally higher than 1024 to reduce sweep overhead on busy gateways.
        /// </remarks>
        public int RunStaleClientCleanupEveryNRequests { get; set; } = 16384;
    }

    /// <summary>
    /// A single throttling step: when requests exceed a threshold in the current window, apply a delay.
    /// </summary>
    public sealed class RequestDelayStep
    {
        /// <summary>
        /// Gets or sets the request-count threshold (exclusive). When the count is greater than this value, the step is active.
        /// </summary>
        public int Exceeds { get; set; }

        /// <summary>
        /// Gets or sets the delay to apply when the step is active.
        /// </summary>
        public int DelayMilliseconds { get; set; }
    }
}
