using System;

using Eigenverft.NetLib.Networking;
using Eigenverft.WebLib.ClientNetwork;
using Eigenverft.WebLib.Middleware.Primitives.Features;

using Microsoft.AspNetCore.Http;

namespace Eigenverft.Routed.RequestFilters.Middleware.Abstractions
{
    internal static class FilterClientNetworkExtensions
    {
        internal static string GetRemoteIpAddress(this HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            return context
                .GetRequiredFeature<IClientNetworkFeature>()
                .RemoteIpAddress
                .ToCanonicalString();
        }
    }
}
