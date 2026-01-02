using System;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Options;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.RequestSignatureFiltering
{
    /// <summary>
    /// Provides configuration options for request signature filtering middleware.
    /// </summary>
    /// <remarks>
    /// This options type is designed to be bound from configuration, for example from a section named
    /// <c>RequestSignatureFilteringOptions</c>. If the section is missing, property initializers act as defaults.
    ///
    /// <para>Example configuration (appsettings.json):</para>
    /// <code>
    /// "RequestSignatureFilteringOptions": {
    ///   "Enabled": true,
    ///   "RequestSignatureSchema": "Version1",
    ///   "SignatureSanitizeTokens": [
    ///     "*",
    ///     "?",
    ///     "#"
    ///   ],
    ///   "SignatureSanitizeReplacement": "_",
    ///   "FilterPriority": "Blacklist",
    ///   "Whitelist": [
    ///     "*"
    ///   ],
    ///   "Blacklist": [
    ///     "*HTTP.Method=POST*Content-Type*multipart/form-data*boundary*bissa*"
    ///   ],
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
    public sealed class RequestSignatureFilteringOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether the middleware is active.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the signature builder variant used to create the observed signature.
        /// </summary>
        public RequestSignatureSchema SignatureVariant { get; set; } = RequestSignatureSchema.Version1;

        /// <summary>
        /// Gets or sets the list of strings that will be replaced in the generated signature.
        /// </summary>
        /// <remarks>
        /// Reviewer note: This is intended to normalize characters that may complicate configuration patterns.
        /// Defaults replace <c>?</c>, <c>*</c>, and <c>#</c>.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> SignatureSanitizeTokens { get; set; } = new[] { "?", "*", "#" };

        /// <summary>
        /// Gets or sets the replacement string used for entries from <see cref="SignatureSanitizeTokens"/>.
        /// </summary>
        public string SignatureSanitizeReplacement { get; set; } = "_";

        /// <summary>
        /// Gets or sets the resolution strategy when a signature pattern matches both the whitelist and the blacklist.
        /// </summary>
        /// <remarks>
        /// When the value is <see cref="FilterPriority.Whitelist"/> the signature is treated as allowed.
        /// When the value is <see cref="FilterPriority.Blacklist"/> the signature is treated as forbidden.
        /// </remarks>
        public FilterPriority FilterPriority { get; set; } = FilterPriority.Blacklist;

        /// <summary>
        /// Gets or sets the list of explicitly allowed signature patterns.
        /// </summary>
        public OptionsConfigOverridesDefaultsList<string> Whitelist { get; set; } = new[] { "*" };

        /// <summary>
        /// Gets or sets the list of explicitly forbidden signature patterns.
        /// </summary>
        public OptionsConfigOverridesDefaultsList<string> Blacklist { get; set; } = new[] { "*HTTP.Method=POST*Content-Type*multipart/form-data*boundary*bissa*" };

        /// <summary>
        /// Gets or sets a value indicating whether signature pattern matching is case sensitive.
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
        public bool RecordUnmatchedRequests { get; set; } = false;

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
