using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Middleware.FileExtensionBlocking
{
    /// <summary>
    /// Provides configuration options for <see cref="FileExtensionBlocking"/>.
    /// </summary>
    /// <remarks>
    /// This options type is designed to be bound from configuration, for example from a section named
    /// <c>FileExtensionBlockingOptions</c>. If the section is missing, property initializers act as defaults.
    /// </remarks>
    public sealed class FileExtensionBlockingOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether the middleware is active.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets a list of file extensions that should be blocked.
        /// </summary>
        /// <remarks>
        /// Matching is performed case-insensitively on the request path suffix.
        /// The default blocks source map requests.
        /// </remarks>
        public string[] Extensions { get; set; } = new[] { ".map" };

        /// <summary>
        /// Gets or sets a list of glob patterns applied to the request path.
        /// </summary>
        /// <remarks>
        /// Supported tokens: <c>*</c>, <c>?</c>, <c>**</c>.
        /// Patterns are matched case-insensitively.
        /// Example: <c>/lib/**</c> or <c>/**/*.map</c>.
        /// </remarks>
        public string[] PathGlobPatterns { get; set; } = System.Array.Empty<string>();

        /// <summary>
        /// Gets or sets a list of regex patterns applied to the request path.
        /// </summary>
        /// <remarks>
        /// Patterns are matched case-insensitively.
        /// Prefer anchoring (for example <c>^/lib/.*\.map$</c>) to avoid accidental matches.
        /// </remarks>
        public string[] PathRegexPatterns { get; set; } = System.Array.Empty<string>();

        /// <summary>
        /// Gets or sets the HTTP status code that is returned when a request is blocked.
        /// </summary>
        public int StatusCode { get; set; } = StatusCodes.Status404NotFound;

        /// <summary>
        /// Gets or sets the log level used when a request is blocked.
        /// </summary>
        /// <remarks>
        /// Use <see cref="LogLevel.None"/> to disable logging for blocked requests.
        /// </remarks>
        public LogLevel LogLevel { get; set; } = LogLevel.Debug;
    }
}
