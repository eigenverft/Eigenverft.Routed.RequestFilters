using Eigenverft.Routed.RequestFilters.Options;

using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.RequestLogging
{
    /// <summary>
    /// Provides configuration options for <see cref="RequestLoggingMiddleware"/>.
    /// </summary>
    /// <remarks>
    /// Example configuration snippet:
    /// <code>
    /// "RequestLoggingOptions": {
    ///   "IsEnabled": true,
    ///   "LogLevelDecision": "Debug",
    ///   "LogLevelLogging": "Information",
    ///   "IgnoreRemoteIpPatterns": [ "127.*", "10.*" ],
    ///   "IgnoreUserAgentPatterns": [ "YARP/*" ]
    /// }
    /// </code>
    /// </remarks>
    public sealed class RequestLoggingOptions
    {
        /// <summary>
        /// Gets or sets a global switch to enable or disable request/response logging.
        /// </summary>
        /// <remarks>
        /// Intended for debugging. In production you typically keep this <see langword="false"/>.
        /// </remarks>
        public bool IsEnabled { get; set; } = false;

        /// <summary>
        /// Gets or sets the log level used for the decision and log output.
        /// </summary>
        /// <remarks>
        /// This controls the single decision line indicating whether the request was ignored or logged, and why.
        /// Use <see cref="LogLevel.None"/> to disable decision logging.
        /// </remarks>
        public LogLevel LogLevelDecision { get; set; } = LogLevel.Debug;

        /// <summary>
        /// Gets or sets the log level used for the decision and log output.
        /// </summary>
        /// <remarks>
        /// This controls the single log level used for both request and response logging.
        /// Use <see cref="LogLevel.None"/> to disable request/response logging even when <see cref="IsEnabled"/> is <see langword="true"/>.
        /// </remarks>
        public LogLevel LogLevelLogging { get; set; } = LogLevel.Information;

        /// <summary>
        /// Gets or sets remote ip wildcard patterns that should be ignored (not logged).
        /// </summary>
        /// <remarks>
        /// When configured, a match causes logging to be skipped for the request.
        /// Patterns support <c>*</c>, <c>?</c>, and <c>#</c> (via the shared filter classifier).
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> IgnoreRemoteIpPatterns { get; set; } = new();

        /// <summary>
        /// Gets or sets User-Agent wildcard patterns that should be ignored (not logged).
        /// </summary>
        /// <remarks>
        /// When configured, a match causes logging to be skipped for the request.
        /// Patterns support <c>*</c>, <c>?</c>, and <c>#</c> (via the shared filter classifier).
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> IgnoreUserAgentPatterns { get; set; } = new[] { "YARP/*" };
    }

}
