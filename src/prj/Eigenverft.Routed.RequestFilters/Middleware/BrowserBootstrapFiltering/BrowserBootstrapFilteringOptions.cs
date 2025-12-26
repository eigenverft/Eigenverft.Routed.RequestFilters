using System;

using Eigenverft.Routed.RequestFilters.Options;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.BrowserBootstrapFiltering
{
    /// <summary>
    /// Resolves conflicts when a request path matches both <see cref="BrowserBootstrapFilteringOptions.BootstrapScopePathPatterns"/>
    /// and <see cref="BrowserBootstrapFilteringOptions.BootstrapExceptionPathPatterns"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is mapped to <see cref="FilterPriority"/> when calling
    /// <see cref="FilterClassifier.Classify(string, string[]?, string[]?, bool, FilterPriority)"/>.
    /// </para>
    /// <para>
    /// <see cref="PreferScope"/> maps to <see cref="FilterPriority.Whitelist"/> (scope wins on overlap).
    /// <see cref="PreferException"/> maps to <see cref="FilterPriority.Blacklist"/> (exception wins on overlap).
    /// </para>
    /// </remarks>
    public enum BootstrapConflictPreference
    {
        /// <summary>
        /// If a path matches both scope and exception patterns, treat it as in-scope (bootstrap applies).
        /// </summary>
        PreferScope,

        /// <summary>
        /// If a path matches both scope and exception patterns, treat it as an exception (bootstrap does not apply).
        /// </summary>
        PreferException
    }

    /// <summary>
    /// Provides configuration options for <see cref="BrowserBootstrapFiltering"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stage 1 (path classification):
    /// </para>
    /// <list type="bullet">
    /// <item><description>Scope (Whitelist): bootstrap applies.</description></item>
    /// <item><description>Exception (Blacklist): bootstrap does not apply; policy decides allow/block immediately.</description></item>
    /// <item><description>Unmatched: bootstrap does not apply; policy decides allow/block immediately.</description></item>
    /// </list>
    /// <para>
    /// Stage 2 (bootstrap outcome for in-scope paths only): when the cookie cannot be established,
    /// <see cref="AllowRequiredPathsOnBootstrapFailure"/> decides allow vs block.
    /// </para>
    /// </remarks>
    public sealed class BrowserBootstrapFilteringOptions
    {
        // --------------------------------------------------------------------
        // Stage 1: Path classification (supports exact entries and wildcard patterns)
        // --------------------------------------------------------------------

        /// <summary>
        /// Gets or sets patterns that define the path scope where bootstrap applies.
        /// </summary>
        /// <remarks>
        /// Default: <c>/</c> and <c>/index.html</c>.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> BootstrapScopePathPatterns { get; set; } = new[] { "/", "/index.html" };

        /// <summary>
        /// Gets or sets patterns that define exceptions where bootstrap does not apply.
        /// </summary>
        /// <remarks>
        /// Default: empty.
        /// </remarks>
        public OptionsConfigOverridesDefaultsList<string> BootstrapExceptionPathPatterns { get; set; } = new[] { "*" };

        /// <summary>
        /// Gets or sets how overlaps between scope and exception patterns are resolved.
        /// </summary>
        public BootstrapConflictPreference BootstrapConflictPreference { get; set; } = BootstrapConflictPreference.PreferScope;

        /// <summary>
        /// Gets or sets a value indicating whether path matching is case sensitive.
        /// </summary>
        public bool CaseSensitivePaths { get; set; } = false;

        // --------------------------------------------------------------------
        // Stage 1 policy: What to do when bootstrap does NOT apply (exceptions + unmatched)
        // --------------------------------------------------------------------

        /// <summary>
        /// Gets or sets a value indicating whether requests on exception paths are allowed to pass through.
        /// </summary>
        /// <remarks>
        /// Applies to paths classified as <see cref="FilterMatchKind.Blacklist"/> by the classifier
        /// (i.e., matching <see cref="BootstrapExceptionPathPatterns"/>).
        /// </remarks>
        public bool AllowRequestsOnBootstrapExceptionPaths { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether exception path hits are recorded.
        /// </summary>
        public bool RecordRequestsOnBootstrapExceptionPaths { get; set; } = false;

        /// <summary>
        /// Gets or sets the log level used when a request is classified as an exception path.
        /// </summary>
        public LogLevel LogLevelBootstrapExceptionPaths { get; set; } = LogLevel.None;

        /// <summary>
        /// Gets or sets a value indicating whether requests on unmatched paths are allowed to pass through.
        /// </summary>
        /// <remarks>
        /// Unmatched means the path matched neither scope nor exception patterns.
        /// </remarks>
        public bool AllowRequestsOnUnmatchedPaths { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether unmatched path hits are recorded.
        /// </summary>
        public bool RecordRequestsOnUnmatchedPaths { get; set; } = false;

        /// <summary>
        /// Gets or sets the log level used when a request is classified as unmatched.
        /// </summary>
        public LogLevel LogLevelUnmatchedPaths { get; set; } = LogLevel.None;

        /// <summary>
        /// Gets or sets the log level used when a request is classified as in-scope (bootstrap applies).
        /// </summary>
        /// <remarks>
        /// This is classification-only logging. The allow/block decision is made by the bootstrap flow.
        /// </remarks>
        public LogLevel LogLevelBootstrapScopePaths { get; set; } = LogLevel.None;

        // --------------------------------------------------------------------
        // Bootstrap cookie + blocking
        // --------------------------------------------------------------------

        /// <summary>
        /// Gets or sets the cookie name used as the bootstrap signal.
        /// </summary>
        public string CookieName { get; set; } = "Eigenverft.BrowserBootstrap";

        /// <summary>
        /// Gets or sets the cookie max-age.
        /// </summary>
        public TimeSpan CookieMaxAge { get; set; } = TimeSpan.FromDays(1);

        /// <summary>
        /// Gets or sets the http status code used when the middleware actively blocks a request.
        /// </summary>
        public int BlockStatusCode { get; set; } = StatusCodes.Status400BadRequest;

        // --------------------------------------------------------------------
        // Stage 2: Bootstrap outcome (only relevant for in-scope paths)
        // --------------------------------------------------------------------

        /// <summary>
        /// Gets or sets a value indicating whether in-scope requests are allowed to pass through
        /// when the bootstrap cookie could not be established.
        /// </summary>
        /// <remarks>
        /// Applies only to the internal bootstrap outcome endpoints.
        /// </remarks>
        public bool AllowRequiredPathsOnBootstrapFailure { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether in-scope requests are recorded when bootstrap fails.
        /// </summary>
        /// <remarks>
        /// Applies only to the internal bootstrap outcome endpoints.
        /// </remarks>
        public bool RecordRequiredPathsOnBootstrapFailure { get; set; } = true;

        // --------------------------------------------------------------------
        // Logging related to the bootstrap flow
        // --------------------------------------------------------------------

        /// <summary>
        /// Gets or sets the log level used when the request is allowed to proceed normally
        /// because the cookie already exists (or continue succeeds).
        /// </summary>
        public LogLevel LogLevelAllowedRequests { get; set; } = LogLevel.None;

        /// <summary>
        /// Gets or sets the log level used when the middleware serves the bootstrap attempt HTML.
        /// </summary>
        public LogLevel LogLevelBootstrapAttempt { get; set; } = LogLevel.Information;

        /// <summary>
        /// Gets or sets the log level used for bootstrap outcomes (continue/fail), including allow/block decisions.
        /// </summary>
        public LogLevel LogLevelBootstrapOutcome { get; set; } = LogLevel.Warning;
    }
}
