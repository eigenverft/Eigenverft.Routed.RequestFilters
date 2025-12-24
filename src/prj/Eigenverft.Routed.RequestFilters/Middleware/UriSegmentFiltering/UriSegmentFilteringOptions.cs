using System;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Options;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.UriSegmentFiltering
{
    /// <summary>
    /// Provides configuration options for URI segment filtering middleware.
    /// </summary>
    /// <remarks>
    /// Defaults are defined via property initializers.
    /// When configuration supplies a value (for example <c>Whitelist</c>), the binder replaces the array entirely.
    /// To intentionally clear a default list from configuration, set it to an empty array (<c>[]</c>).
    /// <para>
    /// Default blacklist contains common scanner / IOT / bot probe segments that are frequently requested on public servers.
    /// These defaults are intentionally conservative (high-signal, low-false-positive) and can be replaced via configuration binding.
    /// </para>
    /// <para>
    /// Example configuration snippet:
    /// </para>
    /// <code>
    /// "UriSegmentFilteringOptions": {
    ///   "FilterPriority": "Blacklist",
    ///   "Whitelist": [ "*" ],
    ///   "Blacklist": [ "wp-admin", "wp-login.php", "wp-content", ".env", ".git", "phpmyadmin", "admin", "cgi-bin", "hudson", "jenkins", "actuator" ],
    ///   "CaseSensitive": false,
    ///   "BlockStatusCode": 400,
    ///   "AllowBlacklistedRequests": true,
    ///   "AllowUnmatchedRequests": true,
    ///   "RecordBlacklistedRequests": true,
    ///   "RecordUnmatchedRequests": true,
    ///   "LogLevelWhitelist": "None",
    ///   "LogLevelBlacklist": "Information",
    ///   "LogLevelUnmatched": "Warning"
    /// }
    /// </code>
    /// </remarks>
    public sealed class UriSegmentFilteringOptions
    {
        /// <summary>
        /// Gets or sets the resolution strategy when a segment pattern matches both the whitelist and the blacklist.
        /// </summary>
        /// <remarks>
        /// When the value is <see cref="FilterPriority.Whitelist"/> the segment is treated as allowed.
        /// When the value is <see cref="FilterPriority.Blacklist"/> the segment is treated as forbidden.
        /// </remarks>
        public FilterPriority FilterPriority { get; set; } = FilterPriority.Blacklist;

        /// <summary>
        /// Gets or sets the list of explicitly allowed segment patterns.
        /// </summary>
        /// <remarks>
        /// Default: empty (allow behavior is controlled by <see cref="AllowUnmatchedRequests"/>).
        /// If configuration specifies <c>Whitelist</c>, it fully replaces this value.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> Whitelist { get; set; } = new[] { "*" };

        /// <summary>
        /// Gets or sets the list of explicitly forbidden segment patterns.
        /// </summary>
        /// <remarks>
        /// Default: common high-frequency probe segments (scanners/bots/IOT).
        /// If configuration specifies <c>Blacklist</c>, it fully replaces this value.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> Blacklist { get; set; } = new[]
        {
            // WordPress probes
            "wp-admin",
            "wp-login.php",
            "wp-content",

            // Common secret/config leaks
            ".env",
            ".git",

            // Common admin panels / legacy tooling
            "phpmyadmin",
            "admin",
            "cgi-bin",

            // CI servers / management UIs
            "hudson",
            "jenkins",

            // Spring / Java management endpoints
            "actuator",
        };

        /// <summary>
        /// Gets or sets a value indicating whether segment pattern matching is case sensitive.
        /// </summary>
        /// <remarks>
        /// Default is <see langword="false"/> to reduce bypasses via casing variations and because scanners/IOT devices
        /// often hit the same resource names with inconsistent casing.
        /// </remarks>
        public bool CaseSensitive { get; set; } = false;

        /// <summary>
        /// Gets or sets the http status code that is used when the middleware actively blocks a request.
        /// </summary>
        /// <remarks>
        /// The status code is applied when <see cref="AllowBlacklistedRequests"/> or <see cref="AllowUnmatchedRequests"/> are set to <see langword="false"/>
        /// and the corresponding case occurs.
        /// </remarks>
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
        public LogLevel LogLevelWhitelist { get; set; } = LogLevel.None;

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
