using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.DependencyInjection;

namespace Eigenverft.Routed.RequestFilters.GenericExtensions.IServiceCollectionExtensions
{
    /// <summary>Service collection extensions for HTTPS redirection defaults.</summary>
    public static partial class IServiceCollectionExtensions
    {
        /// <summary>
        /// Registers HTTPS redirection with a permanent redirect status code (308) while leaving port resolution to the framework defaults.
        /// </summary>
        /// <remarks>
        /// This method sets <see cref="HttpsRedirectionOptions.RedirectStatusCode"/> to <see cref="StatusCodes.Status308PermanentRedirect"/>.
        /// It does not set <see cref="HttpsRedirectionOptions.HttpsPort"/>, so the middleware resolves the HTTPS port using its default mechanisms.
        /// If no HTTPS port can be determined at runtime, no redirect is performed.
        /// </remarks>
        /// <param name="services">The service collection to add the registration to.</param>
        /// <param name="configure">Optional additional configuration applied after the default status code is set.</param>
        /// <returns>The same service collection so that additional calls can be chained.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
        /// <example>
        /// <code>
        /// builder.Services.AddPermanentHttpsRedirection();
        /// </code>
        /// </example>
        public static IServiceCollection AddPermanentHttpsRedirection(this IServiceCollection services, Action<HttpsRedirectionOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            return services.AddHttpsRedirection(options =>
            {
                options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
                configure?.Invoke(options);
            });
        }

        /// <summary>
        /// Registers HTTPS redirection with a permanent redirect status code (308) and a fixed HTTPS port.
        /// </summary>
        /// <remarks>
        /// This method sets <see cref="HttpsRedirectionOptions.RedirectStatusCode"/> to <see cref="StatusCodes.Status308PermanentRedirect"/>
        /// and sets <see cref="HttpsRedirectionOptions.HttpsPort"/> to <paramref name="httpsPort"/>.
        /// Use this overload if automatic port resolution is ambiguous (for example, multiple HTTPS endpoints) or unavailable.
        /// </remarks>
        /// <param name="services">The service collection to add the registration to.</param>
        /// <param name="httpsPort">The HTTPS port used for redirects.</param>
        /// <param name="configure">Optional additional configuration applied after defaults and <paramref name="httpsPort"/> are set.</param>
        /// <returns>The same service collection so that additional calls can be chained.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="httpsPort"/> is outside the valid range 1..65535.</exception>
        /// <example>
        /// <code>
        /// builder.Services.AddPermanentHttpsRedirection(443);
        /// </code>
        /// </example>
        public static IServiceCollection AddPermanentHttpsRedirection(this IServiceCollection services, int httpsPort, Action<HttpsRedirectionOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            if (httpsPort < 1 || httpsPort > 65535) throw new ArgumentOutOfRangeException(nameof(httpsPort), httpsPort, "Port must be in range 1..65535.");

            return services.AddHttpsRedirection(options =>
            {
                options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
                options.HttpsPort = httpsPort;
                configure?.Invoke(options);
            });
        }
    }
}
