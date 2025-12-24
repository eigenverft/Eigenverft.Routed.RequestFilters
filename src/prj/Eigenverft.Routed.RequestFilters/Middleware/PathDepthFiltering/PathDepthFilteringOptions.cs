// ============================================================================
// File: PathDepthFilteringOptions.cs
// Namespace: Eigenverft.Routed.RequestFilters.Middleware.PathDepthFiltering
// ============================================================================

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.PathDepthFiltering
{
    /// <summary>
    /// Provides configuration options for path depth filtering middleware.
    /// </summary>
    /// <remarks>
    /// This middleware has no "unmatched" concept. Requests are either within the limit (treated as allowed)
    /// or exceed the limit (treated as blacklisted).
    /// <para>
    /// Example configuration snippet:
    /// </para>
    /// <code>
    /// "PathDepthFilteringOptions": {
    ///   "PathDepthLimit": 6,
    ///   "BlockStatusCode": 400,
    ///   "AllowBlacklistedRequests": true,
    ///   "RecordBlacklistedRequests": true,
    ///   "LogLevelWhitelist": "Debug",
    ///   "LogLevelBlacklist": "Information"
    /// }
    /// </code>
    /// </remarks>
    public sealed class PathDepthFilteringOptions
    {
        /// <summary>
        /// Gets or sets the maximum allowed path depth.
        /// </summary>
        public int PathDepthLimit { get; set; } = 6;

        /// <summary>
        /// Gets or sets the http status code that is used when the middleware actively blocks a request.
        /// </summary>
        public int BlockStatusCode { get; set; } = StatusCodes.Status400BadRequest;

        /// <summary>
        /// Gets or sets a value indicating whether requests classified as blacklisted (depth exceeds limit) are still allowed to pass through.
        /// </summary>
        public bool AllowBlacklistedRequests { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether blacklist hits are recorded.
        /// </summary>
        public bool RecordBlacklistedRequests { get; set; } = true;

        /// <summary>
        /// Gets or sets the log level used when the request is treated as allowed (depth within limit).
        /// </summary>
        public LogLevel LogLevelWhitelist { get; set; } = LogLevel.Debug;

        /// <summary>
        /// Gets or sets the log level used when the request is treated as blacklisted (depth exceeds limit).
        /// </summary>
        public LogLevel LogLevelBlacklist { get; set; } = LogLevel.Information;
    }
}
