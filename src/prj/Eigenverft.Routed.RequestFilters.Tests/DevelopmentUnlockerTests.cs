using System.IO;
using System.Net;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Middleware.DevelopmentUnlocker;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Tests
{
    [TestClass]
    public sealed class DevelopmentUnlockerTests
    {
        [TestMethod]
        public async Task EndpointRemovesEventsForCanonicalCallerAddress()
        {
            const string callerIp = "203.0.113.21";
            UnlockResult result = await ExecuteAsync(
                callerIp,
                "/maintenance/unlock",
                callerIp,
                options => options.EndpointPath = "maintenance/unlock");

            Assert.IsFalse(result.NextCalled);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            Assert.AreEqual(0, result.TargetBlacklistCount);
        }

        [TestMethod]
        public async Task OverrideRemovesEventsForCanonicalizedMappedIpv4AddressOnly()
        {
            const string callerIp = "203.0.113.22";
            const string targetIp = "192.0.2.42";
            UnlockResult result = await ExecuteAsync(
                callerIp,
                "/maintenance/unlock/[::ffff:192.0.2.42]",
                targetIp,
                options => options.EndpointPath = "/maintenance/unlock",
                seedCaller: true);

            Assert.IsFalse(result.NextCalled);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            Assert.AreEqual(0, result.TargetBlacklistCount);
            Assert.AreEqual(1, result.CallerBlacklistCount);
        }

        [TestMethod]
        public async Task InvalidOverrideReturnsBadRequestWithoutRemovingEvents()
        {
            const string callerIp = "203.0.113.23";
            const string targetIp = "192.0.2.43";
            UnlockResult result = await ExecuteAsync(
                callerIp,
                "/maintenance/unlock/not-an-ip",
                targetIp,
                options => options.EndpointPath = "/maintenance/unlock");

            Assert.IsFalse(result.NextCalled);
            Assert.AreEqual(StatusCodes.Status400BadRequest, result.StatusCode);
            Assert.AreEqual(1, result.TargetBlacklistCount);
        }

        [TestMethod]
        public async Task DisabledUnlockerPassesMatchingRequestThrough()
        {
            const string callerIp = "203.0.113.24";
            UnlockResult result = await ExecuteAsync(
                callerIp,
                "/maintenance/unlock",
                callerIp,
                options =>
                {
                    options.EndpointPath = "/maintenance/unlock";
                    options.Enabled = false;
                });

            Assert.IsTrue(result.NextCalled);
            Assert.AreEqual(1, result.TargetBlacklistCount);
        }

        private static async Task<UnlockResult> ExecuteAsync(
            string callerIp,
            string requestPath,
            string targetIp,
            System.Action<DevelopmentUnlockerOptions> configure,
            bool seedCaller = false)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
            services.AddDevelopmentUnlocker(configure);
            services.AddFilteringEventStorage<InMemoryStorage>()
                .Configure(options => options.MemoryLimitBytes = 0);

            using ServiceProvider provider = services.BuildServiceProvider();
            IFilteringEventStorage storage = provider.GetRequiredService<IFilteringEventStorage>();
            await storage.StoreAsync(CreateBlacklistEvent(targetIp));
            if (seedCaller && callerIp != targetIp)
            {
                await storage.StoreAsync(CreateBlacklistEvent(callerIp));
            }

            var app = new ApplicationBuilder(provider);
            bool nextCalled = false;
            app.UseDevelopmentUnlocker();
            app.Run(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            RequestDelegate pipeline = app.Build();
            var context = new DefaultHttpContext
            {
                RequestServices = provider,
            };
            context.Connection.RemoteIpAddress = IPAddress.Parse(callerIp);
            context.Request.Path = requestPath;
            context.Response.Body = new MemoryStream();

            await pipeline(context);

            return new UnlockResult(
                nextCalled,
                context.Response.StatusCode,
                storage.GetBlacklistCount(targetIp),
                storage.GetBlacklistCount(callerIp));
        }

        private static FilteringEvent CreateBlacklistEvent(string remoteIpAddress)
        {
            return new FilteringEvent
            {
                RemoteIpAddress = remoteIpAddress,
                EventSource = "DevelopmentUnlockerTests",
                MatchKind = FilterMatchKind.Blacklist,
            };
        }

        private sealed record UnlockResult(
            bool NextCalled,
            int StatusCode,
            int TargetBlacklistCount,
            int CallerBlacklistCount);
    }
}
