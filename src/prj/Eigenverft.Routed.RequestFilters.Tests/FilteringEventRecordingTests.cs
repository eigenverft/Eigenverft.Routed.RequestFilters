using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Middleware.HttpMethodFiltering;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent.FilteringStorage.InMemoryFiltering;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Tests
{
    [TestClass]
    public sealed class FilteringEventRecordingTests
    {
        [TestMethod]
        public async Task DuplicateDefaultRegistrationRecordsEachFilterEventOnlyOnce()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
            services.AddHttpMethodFiltering(options =>
            {
                options.Whitelist = new List<string> { "GET" };
                options.Blacklist = new List<string> { "TRACE" };
                options.AllowBlacklistedRequests = true;
                options.AllowUnmatchedRequests = true;
                options.RecordBlacklistedRequests = true;
                options.RecordUnmatchedRequests = true;
                options.LogLevelBlacklist = LogLevel.None;
                options.LogLevelUnmatched = LogLevel.None;
            });
            services.AddFilteringEventStorage<InMemoryStorage>()
                .Configure((InMemoryFilteringEventStorageOptions options) => options.MemoryLimitBytes = 0);

            using ServiceProvider provider = services.BuildServiceProvider();
            var app = new ApplicationBuilder(provider);
            app.UseHttpMethodFiltering();
            app.UseHttpMethodFiltering();
            app.Run(_ => Task.CompletedTask);
            RequestDelegate pipeline = app.Build();

            await InvokeAsync(pipeline, provider, "TRACE");
            await InvokeAsync(pipeline, provider, "CUSTOM");

            IFilteringEventStorage storage = provider.GetRequiredService<IFilteringEventStorage>();
            const string canonicalIp = "192.0.2.45";
            Assert.AreEqual(1, storage.GetBlacklistCount(canonicalIp));
            Assert.AreEqual(1, storage.GetUnmatchedCount(canonicalIp));

            FilteringEventBySourceAggregate source = storage.GetByEventSource(canonicalIp).Single();
            Assert.AreEqual(nameof(HttpMethodFiltering), source.EventSource);
            Assert.AreEqual(2L, source.Count);
        }

        private static async Task InvokeAsync(RequestDelegate pipeline, ServiceProvider provider, string method)
        {
            var context = new DefaultHttpContext
            {
                RequestServices = provider,
            };
            context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.0.2.45");
            context.Request.Method = method;
            context.Response.Body = new MemoryStream();
            await pipeline(context);
        }
    }
}
