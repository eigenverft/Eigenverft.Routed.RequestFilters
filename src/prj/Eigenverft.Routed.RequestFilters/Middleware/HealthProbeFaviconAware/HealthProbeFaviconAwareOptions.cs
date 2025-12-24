namespace Eigenverft.Routed.RequestFilters.Middleware.HealthProbeFaviconAware
{
    /// <summary>
    /// Provides configuration options for <see cref="HealthProbeFaviconAware"/>.
    /// </summary>
    /// <remarks>
    /// Bindable from configuration section <c>HealthProbeOptions</c>. If the section is missing,
    /// property initializers act as defaults.
    /// </remarks>
    public sealed class HealthProbeFaviconAwareOptions
    {
        /// <summary>
        /// Gets or sets the request path that triggers the health response.
        /// </summary>
        public string Path { get; set; } = "/health";

        /// <summary>
        /// Gets or sets the response body written for non-HEAD health requests.
        /// </summary>
        public string ResponseBody { get; set; } = "OK";
    }
}
