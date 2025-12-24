using System;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Options;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.CidrFiltering
{
    /// <summary>
    /// Provides configuration options for CIDR filtering middleware.
    /// </summary>
    /// <remarks>
    /// Defaults are defined via property initializers.
    /// When configuration supplies a value (for example <c>Whitelist</c>), the binder replaces the array entirely.
    /// To intentionally clear a default list from configuration, set it to an empty array (<c>[]</c>).
    /// <para>
    /// CIDR entries are expected in standard notation for IPv4 or IPv6, for example <c>192.168.1.0/24</c> or <c>2001:db8::/64</c>.
    /// The special entry <c>*</c> matches all IPs in the corresponding list.
    /// </para>
    /// <para>
    /// Example configuration snippet:
    /// </para>
    /// <code>
    /// "CidrFilteringOptions": {
    ///   "FilterPriority": "Whitelist",
    ///
    ///   // Allow only internal networks.
    ///   "Whitelist": [ "10.0.0.0/8", "192.168.0.0/16", "fd00::/8" ],
    ///
    ///   // Everything else will be treated as blacklisted (match-all deny).
    ///   "Blacklist": [ "*" ],
    ///
    ///   "BlockStatusCode": 400,
    ///   "AllowBlacklistedRequests": true,
    ///   "AllowUnmatchedRequests": true,
    ///   "RecordBlacklistedRequests": true,
    ///   "RecordUnmatchedRequests": true,
    ///   "LogLevelWhitelist": "Debug",
    ///   "LogLevelBlacklist": "Information",
    ///   "LogLevelUnmatched": "Warning"
    /// }
    /// </code>
    /// </remarks>
    public sealed class CidrFilteringOptions
    {
        /// <summary>
        /// Gets or sets the resolution strategy when the remote IP is contained in both lists.
        /// </summary>
        /// <remarks>
        /// When the value is <see cref="FilterPriority.Whitelist"/> the request is treated as allowed.
        /// When the value is <see cref="FilterPriority.Blacklist"/> the request is treated as forbidden.
        /// </remarks>
        public FilterPriority FilterPriority { get; set; } = FilterPriority.Whitelist;

        /// <summary>
        /// Gets or sets the list of explicitly allowed CIDR ranges.
        /// </summary>
        /// <remarks>
        /// Default: internal network ranges for typical intranet scenarios.
        /// If configuration specifies <c>Whitelist</c>, it fully replaces this value.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> Whitelist { get; set; } = new[]
        {
            "10.0.0.0/8",
            "192.168.0.0/16",
            "fd00::/8"
        };

        /// <summary>
        /// Gets or sets the list of explicitly forbidden CIDR ranges.
        /// </summary>
        /// <remarks>
        /// Default: match-all deny (<c>*</c>) to enforce "intranet only".
        /// If configuration specifies <c>Blacklist</c>, it fully replaces this value.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> Blacklist { get; set; } = new[]
        {
            "*"
        };

        /// <summary>
        /// Gets or sets the http status code that is used when the middleware actively blocks a request.
        /// </summary>
        public int BlockStatusCode { get; set; } = StatusCodes.Status400BadRequest;

        /// <summary>
        /// Gets or sets a value indicating whether requests classified as <see cref="FilterMatchKind.Blacklist"/> are still allowed to pass through.
        /// </summary>
        public bool AllowBlacklistedRequests { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether requests classified as <see cref="FilterMatchKind.Unmatched"/> are allowed to pass through.
        /// </summary>
        public bool AllowUnmatchedRequests { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether blacklist hits are recorded.
        /// </summary>
        public bool RecordBlacklistedRequests { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether unmatched hits are recorded.
        /// </summary>
        public bool RecordUnmatchedRequests { get; set; } = true;

        /// <summary>
        /// Gets or sets the log level used when the request matches the whitelist.
        /// </summary>
        public LogLevel LogLevelWhitelist { get; set; } = LogLevel.Debug;

        /// <summary>
        /// Gets or sets the log level used when the request matches the blacklist.
        /// </summary>
        public LogLevel LogLevelBlacklist { get; set; } = LogLevel.Information;

        /// <summary>
        /// Gets or sets the log level used when the request is unmatched.
        /// </summary>
        public LogLevel LogLevelUnmatched { get; set; } = LogLevel.Warning;
    }
}
