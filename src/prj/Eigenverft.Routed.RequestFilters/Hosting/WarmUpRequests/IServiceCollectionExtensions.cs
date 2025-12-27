using System;
using System.Net;
using System.Net.Http;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Hosting.WarmUpRequests
{
    public static partial class IServiceCollectionExtensions
    {
        public static IServiceCollection AddWarmUpRequests(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            AddInfrastructure(services);

            services.AddOptions<WarmUpRequestsOptions>().BindConfiguration(nameof(WarmUpRequestsOptions));

            services.AddHttpClient(WarmUpRequestsOptions.HttpClientName)
                .ConfigureHttpClient((sp, client) =>
                {
                    WarmUpRequestsOptions o = sp.GetRequiredService<IOptionsMonitor<WarmUpRequestsOptions>>().CurrentValue;

                    // Reviewer note: enforce an overall timeout in addition to per-request CancellationToken.
                    client.Timeout = o.RequestTimeout > TimeSpan.Zero ? o.RequestTimeout : TimeSpan.FromSeconds(5);
                })
                .ConfigurePrimaryHttpMessageHandler(sp =>
                {
                    WarmUpRequestsOptions o = sp.GetRequiredService<IOptionsMonitor<WarmUpRequestsOptions>>().CurrentValue;

                    return new SocketsHttpHandler
                    {
                        UseProxy = !o.DisableSystemProxy,
                        Proxy = null,
                        ConnectTimeout = o.ConnectTimeout > TimeSpan.Zero ? o.ConnectTimeout : TimeSpan.FromSeconds(2),

                        // Reviewer note: warm-up should not chase redirects; it can mask loops.
                        AllowAutoRedirect = false,

                        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
                    };
                });

            services.AddHostedService<WarmUpRequestsHostedService>();

            return services;
        }

        // ... keep your other overloads

        private static void AddInfrastructure(IServiceCollection services)
        {
            services.TryAddSingleton(typeof(IDeferredLogger<>), typeof(DeferredLogger<>));
            services.AddOptions();
        }
    }
}
