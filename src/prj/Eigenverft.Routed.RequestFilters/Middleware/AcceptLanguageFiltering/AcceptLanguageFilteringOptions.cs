using System;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Options;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.AcceptLanguageFiltering
{
    /// <summary>
    /// Provides configuration options for Accept-Language filtering middleware.
    /// </summary>
    /// <remarks>
    /// Defaults are defined via property initializers.
    /// When configuration supplies a value (for example <c>Whitelist</c>), the binder replaces the array entirely.
    /// To intentionally clear a default list from configuration, set it to an empty array (<c>[]</c>).
    /// <para>
    /// The observed value is the first <c>Accept-Language</c> header value (if present). Missing header is treated as <see cref="string.Empty"/>.
    /// </para>
    /// <para>
    /// Example configuration snippet:
    /// </para>
    /// <code>
    /// "AcceptLanguageFilteringOptions": {
    ///   "FilterPriority": "Blacklist",
    ///   "Whitelist": [ "*" ],
    ///   "Blacklist": [ "", "*zh-CN*", "*zh-*", "*-CN*" ],
    ///   "CaseSensitive": false,
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
    public sealed class AcceptLanguageFilteringOptions
    {
        /// <summary>
        /// Gets or sets the resolution strategy when a pattern matches both the whitelist and the blacklist.
        /// </summary>
        /// <remarks>
        /// When the value is <see cref="FilterPriority.Whitelist"/> the request is treated as allowed.
        /// When the value is <see cref="FilterPriority.Blacklist"/> the request is treated as forbidden.
        /// </remarks>
        public FilterPriority FilterPriority { get; set; } = FilterPriority.Blacklist;

        /// <summary>
        /// Gets or sets the list of explicitly allowed Accept-Language patterns.
        /// </summary>
        /// <remarks>
        /// Default: empty (allow behavior is controlled by <see cref="AllowUnmatchedRequests"/>).
        /// If configuration specifies <c>Whitelist</c>, it fully replaces this value.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> Whitelist { get; set; } = new[] { "*" };

        /// <summary>
        /// Gets or sets the list of explicitly forbidden Accept-Language patterns.
        /// </summary>
        /// <remarks>
        /// Default: empty.
        /// If configuration specifies <c>Blacklist</c>, it fully replaces this value.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> Blacklist { get; set; } = new[] { "", "*zh-CN*", "*zh-*", "*-CN*" };

        /// <summary>
        /// Gets or sets a value indicating whether pattern matching is case sensitive.
        /// </summary>
        /// <remarks>
        /// Default is <see langword="false"/> because language tags are not consistently cased across clients.
        /// </remarks>
        public bool CaseSensitive { get; set; } = false;

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
