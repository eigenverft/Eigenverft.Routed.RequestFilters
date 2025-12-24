using System;

using Eigenverft.Routed.RequestFilters.GenericExtensions.HttpContextExtensions;

using Microsoft.AspNetCore.Http;

namespace Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext
{
    /// <summary>
    /// Provides extension methods for working with a normalized remote IP address on <see cref="HttpContext"/>.
    /// </summary>
    public static partial class HttpContextExtension
    {
        public const string HttpContextRemoteIpAddressKey = "RemoteIpAddress";

        public const string HttpContextRemoteIpAddressStartTimeKey = "RemoteIpAddressStartTime";

        /// <summary>
        /// Sets the normalized remote IP address for the given <see cref="HttpContext"/>.
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        /// <param name="normalizedRemoteIp">The normalized remote IP address value to store.</param>
        public static void SetRemoteIpAddress(this HttpContext context, string normalizedRemoteIp)
        {
            context.SetContextItem(HttpContextRemoteIpAddressKey, normalizedRemoteIp);
        }

        /// <summary>
        /// Gets the normalized remote IP address for the given <see cref="HttpContext"/>.
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        /// <returns>
        /// The normalized remote IP address value associated with the context.
        /// </returns>
        public static string GetRemoteIpAddress(this HttpContext context)
        {
            return context.GetContextItem<string>(HttpContextRemoteIpAddressKey);
        }

        /// <summary>
        /// Sets the normalized remote IP address for the given <see cref="HttpContext"/>.
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        /// <param name="normalizedRemoteIp">The normalized remote IP address value to store.</param>
        public static void SetRemoteIpAddressStartTime(this HttpContext context, DateTime normalizedRemoteIp)
        {
            context.SetContextItem(HttpContextRemoteIpAddressStartTimeKey, normalizedRemoteIp);
        }

        /// <summary>
        /// Gets the normalized remote IP address for the given <see cref="HttpContext"/>.
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        /// <returns>
        /// The normalized remote IP address value associated with the context.
        /// </returns>
        public static DateTime GetRemoteIpAddressStartTime(this HttpContext context)
        {
            return context.GetContextItem<DateTime>(HttpContextRemoteIpAddressStartTimeKey);
        }
    }
}
