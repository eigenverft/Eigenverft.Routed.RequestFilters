using System;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.GenericExtensions.HttpResponseExtensions;
using Eigenverft.Routed.RequestFilters.GenericExtensions.IPAddressExtensions;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.AspNetCore.Http;

namespace Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext
{
    /// <summary>
    /// Middleware that resolves the remote IP address for each HTTP request, normalizes it, and exposes the normalized value via the HTTP context.
    /// </summary>
    /// <remarks>
    /// The middleware reads the remote IP address from <see cref="HttpContext.Connection"/>,
    /// obtains a normalized textual representation via <see cref="GetIpInfo.GetIpInfo(System.Net.IPAddress?)"/>,
    /// and stores the resulting string in <see cref="HttpContext.Items"/> under <see cref="HttpContextItemKey"/>.
    ///
    /// <para>Example registration:</para>
    /// <code>
    /// app.UseMiddleware&lt;RemoteIpAddressContextMiddleware&gt;();
    /// </code>
    ///
    /// <para>Example usage later in the pipeline:</para>
    /// <code>
    /// var remoteIp = context.GetItemOrDefault&lt;string&gt;(RemoteIpAddressContextMiddleware.HttpContextItemKey);
    /// if (!string.IsNullOrWhiteSpace(remoteIp))
    /// {
    ///     logger.LogInformation("Request from {RemoteIp}.", remoteIp);
    /// }
    /// </code>
    /// </remarks>
    public class RemoteIpAddressContextMiddleware
    {
        /// <summary>
        /// The key used to store the normalized remote IP address in <see cref="HttpContext.Items"/>.
        /// </summary>
        /// <remarks>
        /// Use this key together with the <c>GetItem</c> or <c>GetItemOrDefault</c> extensions
        /// when reading the value in downstream middleware or endpoints.
        ///
        /// <para>Example:</para>
        /// <code>
        /// var remoteIp = context.GetItemOrDefault&lt;string&gt;(RemoteIpAddressContextMiddleware.HttpContextItemKey);
        /// </code>
        /// </remarks>


        private readonly RequestDelegate _nextMiddleware;
        private readonly IDeferredLogger<RemoteIpAddressContextMiddleware> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteIpAddressContextMiddleware"/> class.
        /// </summary>
        /// <remarks>
        /// This middleware requires a valid <see cref="RequestDelegate"/> to continue the pipeline
        /// and a logger instance for diagnostics.
        ///
        /// <para>Example:</para>
        /// <code>
        /// app.UseMiddleware&lt;RemoteIpAddressContextMiddleware&gt;();
        /// </code>
        /// </remarks>
        /// <param name="nextMiddleware">The next middleware in the request pipeline.</param>
        /// <param name="logger">The logger used to record diagnostic messages.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="nextMiddleware"/> or <paramref name="logger"/> is <see langword="null"/>.
        /// </exception>
        public RemoteIpAddressContextMiddleware(RequestDelegate nextMiddleware, IDeferredLogger<RemoteIpAddressContextMiddleware> logger)
        {
            _nextMiddleware = nextMiddleware ?? throw new ArgumentNullException(nameof(nextMiddleware));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Invokes the middleware to resolve, normalize, and store the remote IP address.
        /// </summary>
        /// <remarks>
        /// The middleware uses <see cref="HttpContext.Connection.RemoteIpAddress"/> and
        /// <see cref="GetIpInfo.GetIpInfo(System.Net.IPAddress?)"/> to determine a normalized textual
        /// representation of the client IP.
        ///
        /// If a valid representation cannot be determined, the middleware logs an error, writes
        /// a status code 400 response via <see cref="HttpResponseExtensions.WriteDefaultStatusCodeAnswerEx"/>,
        /// and short-circuits the pipeline.
        ///
        /// <para>Example:</para>
        /// <code>
        /// // Inside the middleware pipeline, after registration:
        /// var remoteIp = context.GetItemOrDefault&lt;string&gt;(RemoteIpAddressContextMiddleware.HttpContextItemKey);
        /// </code>
        /// </remarks>
        /// <param name="context">The HTTP context for the current request.</param>
        /// <returns>
        /// A task that completes when the middleware has finished processing the request.
        /// </returns>
        public async Task InvokeAsync(HttpContext context)
        {
            var (_, normalizedRemoteIp) = context.Connection.RemoteIpAddress.GetIpInfo();

            if (string.IsNullOrWhiteSpace(normalizedRemoteIp))
            {
                _logger.LogError("Request rejected: missing valid remote IP address for connection {ConnectionId}. Aborting.", () => context.Connection.Id);
                await context.Response.WriteDefaultStatusCodeAnswerEx(StatusCodes.Status400BadRequest);
                return;
            }

            context.SetRemoteIpAddress(normalizedRemoteIp);
            context.SetRemoteIpAddressStartTime(DateTime.UtcNow);
            await _nextMiddleware(context);
        }
    }
}
