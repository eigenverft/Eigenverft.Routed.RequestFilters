using System;

using Microsoft.AspNetCore.Builder;

namespace Eigenverft.Routed.RequestFilters.Middleware.HealthProbeFaviconAware
{
    /// <summary>
    /// Provides extension methods for registering <see cref="HealthProbeFaviconAware"/> in the application's request pipeline.
    /// </summary>
    public static class HealthProbeFaviconAwareExtensions
    {
        /// <summary>
        /// Adds the <see cref="HealthProbeFaviconAware"/> into the request pipeline.
        /// </summary>
        public static IApplicationBuilder UseHealthProbeFaviconAware(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            return app.UseMiddleware<HealthProbeFaviconAware>();
        }
    }
}