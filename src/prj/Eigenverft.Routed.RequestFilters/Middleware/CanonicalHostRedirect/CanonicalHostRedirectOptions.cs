using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.CanonicalHostRedirect
{
    /// <summary>
    /// Determines how host canonicalization is applied.
    /// </summary>
    public enum CanonicalHostMode
    {
        /// <summary>
        /// Do not change the host; only enforce scheme if configured.
        /// </summary>
        None = 0,

        /// <summary>
        /// Redirect to the apex host, for example <c>eigenverft.com</c>.
        /// </summary>
        ToApex = 1,

        /// <summary>
        /// Redirect to the www host, for example <c>www.eigenverft.com</c>.
        /// </summary>
        ToWww = 2
    }

    /// <summary>
    /// Provides configuration options for <see cref="CanonicalHostRedirect"/>.
    /// </summary>
    /// <remarks>
    /// This options type is designed to be bound from configuration, for example from a section named
    /// <c>CanonicalHostRedirectOptions</c>. If the section is missing, property initializers act as defaults.
    /// </remarks>
    public sealed class CanonicalHostRedirectOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether the middleware is active.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the main apex host that defines the canonical identity, for example <c>eigenverft.com</c>.
        /// </summary>
        public string PrimaryApexHost { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets additional inbound aliases that should be redirected to the canonical host derived from
        /// <see cref="PrimaryApexHost"/> and <see cref="Canonicalization"/>.
        /// </summary>
        public string[] RedirectFromHosts { get; set; } = System.Array.Empty<string>();

        /// <summary>
        /// Gets or sets the canonicalization strategy for the host name.
        /// </summary>
        public CanonicalHostMode Canonicalization { get; set; } = CanonicalHostMode.ToWww;

        /// <summary>
        /// Gets or sets a value indicating whether HTTPS should be enforced for redirected requests.
        /// </summary>
        public bool EnforceHttps { get; set; } = true;

        /// <summary>
        /// Gets or sets the HTTP status code used for the permanent redirect.
        /// </summary>
        /// <remarks>
        /// Default is 308 which preserves method and body.
        /// </remarks>
        public int RedirectStatusCode { get; set; } = StatusCodes.Status308PermanentRedirect;

        /// <summary>
        /// Gets or sets the log level used when a redirect is issued.
        /// </summary>
        /// <remarks>
        /// Use <see cref="LogLevel.None"/> to disable redirect logging.
        /// </remarks>
        public LogLevel LogLevelRedirect { get; set; } = LogLevel.Information;
    }
}
