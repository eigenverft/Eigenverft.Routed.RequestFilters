using System;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Options;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressFiltering
{
    /// <summary>
    /// Provides configuration options for remote ip address filtering middleware.
    /// </summary>
    /// <remarks>
    /// Defaults are defined via property initializers.
    /// When configuration supplies a value (for example <c>Whitelist</c>), the binder replaces the array entirely.
    /// To intentionally clear a default list from configuration, set it to an empty array (<c>[]</c>).
    /// </remarks>
    public sealed class RemoteIpAddressFilteringOptions
    {
        /// <summary>
        /// Gets or sets the resolution strategy when a remote ip address pattern matches both the whitelist and the blacklist.
        /// </summary>
        /// <remarks>
        /// When the value is <see cref="FilterPriority.Whitelist"/> the remote ip address is treated as allowed.
        /// When the value is <see cref="FilterPriority.Blacklist"/> the remote ip address is treated as forbidden.
        /// </remarks>
        public FilterPriority FilterPriority { get; set; } = FilterPriority.Whitelist;

        /// <summary>
        /// Gets or sets the list of explicitly allowed remote ip address patterns.
        /// </summary>
        /// <remarks>
        /// Default: common private/local IPv4 ranges.
        /// If configuration specifies <c>Whitelist</c>, it fully replaces this value.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> Whitelist { get; set; } = new[]
        {
            "127.*",
            "10.*",

            // RFC1918: 172.16.0.0/12 (172.16.* .. 172.31.*)
            "172.16.*",
            "172.17.*",
            "172.18.*",
            "172.19.*",
            "172.20.*",
            "172.21.*",
            "172.22.*",
            "172.23.*",
            "172.24.*",
            "172.25.*",
            "172.26.*",
            "172.27.*",
            "172.28.*",
            "172.29.*",
            "172.30.*",
            "172.31.*",

            "192.168.*",
        };

        /// <summary>
        /// Gets or sets the list of explicitly forbidden remote ip address patterns.
        /// </summary>
        /// <remarks>
        /// Default: one example entry. If configuration specifies <c>Blacklist</c>, it fully replaces this value.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> Blacklist { get; set; } = new[] { "8.8.8.8" };

        /// <summary>
        /// Gets or sets a value indicating whether ip address pattern matching is case sensitive.
        /// </summary>
        public bool CaseSensitive { get; set; } = false;

        /// <summary>
        /// Gets or sets the http status code that is used when the middleware actively blocks a request.
        /// </summary>
        /// <remarks>
        /// The status code is applied when <see cref="AllowBlacklistedRequests"/> or <see cref="AllowUnmatchedRequests"/> are set to <c>false</c>
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
        /// <remarks>
        /// Use <see cref="LogLevel.None"/> to disable logging for whitelist matches.
        /// </remarks>
        public LogLevel LogLevelWhitelist { get; set; } = LogLevel.Debug;

        /// <summary>
        /// Gets or sets the log level used when the request matches the blacklist.
        /// </summary>
        /// <remarks>
        /// Use <see cref="LogLevel.None"/> to disable logging for blacklist matches.
        /// </remarks>
        public LogLevel LogLevelBlacklist { get; set; } = LogLevel.Information;

        /// <summary>
        /// Gets or sets the log level used when the request is unmatched.
        /// </summary>
        /// <remarks>
        /// Use <see cref="LogLevel.None"/> to disable logging for unmatched results.
        /// </remarks>
        public LogLevel LogLevelUnmatched { get; set; } = LogLevel.Warning;
    }
}
