using System;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Options;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.HostNameFiltering
{
    /// <summary>
    /// Provides configuration options for host name filtering middleware.
    /// </summary>
    /// <remarks>
    /// This options type is designed to be bound from configuration, for example from a section named
    /// <c>HostNameFilteringOptions</c>. If the section is missing, property initializers act as defaults.
    /// </remarks>
    public sealed class HostNameFilteringOptions
    {
        /// <summary>
        /// Gets or sets the resolution strategy when a host name pattern matches both the whitelist and the blacklist.
        /// </summary>
        /// <remarks>
        /// When the value is <see cref="FilterPriority.Whitelist"/> the host name is treated as allowed.
        /// When the value is <see cref="FilterPriority.Blacklist"/> the host name is treated as forbidden.
        /// </remarks>
        public FilterPriority FilterPriority { get; set; } = FilterPriority.Whitelist;

        /// <summary>
        /// Gets or sets the list of explicitly allowed host name patterns.
        /// </summary>
        /// <remarks>
        /// Defaults to an empty list. If configuration omits this property, the default remains in effect.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> Whitelist { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the list of explicitly forbidden host name patterns.
        /// </summary>
        /// <remarks>
        /// Defaults to an empty list. If configuration omits this property, the default remains in effect.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> Blacklist { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets a value indicating whether host name pattern matching is case sensitive.
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
