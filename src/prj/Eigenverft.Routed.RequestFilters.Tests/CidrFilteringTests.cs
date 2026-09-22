using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Middleware.CidrFiltering;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Tests
{
    [TestClass]
    public sealed class CidrFilteringTests
    {
        [TestMethod]
        [DataRow("192.0.2.42", "192.0.2.0/24")]
        [DataRow("::ffff:192.0.2.42", "192.0.2.0/24")]
        [DataRow("2001:db8::42", "2001:db8::/64")]
        public async Task WhitelistSupportsIpv4MappedIpv6AndNativeIpv6(string remoteIp, string allowedCidr)
        {
            PipelineResult result = await ExecuteAsync(
                remoteIp,
                new[] { allowedCidr },
                new[] { "*" },
                FilterPriority.Whitelist);

            Assert.IsTrue(result.NextCalled);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
        }

        [TestMethod]
        public async Task InvalidCidrDoesNotMatch()
        {
            PipelineResult result = await ExecuteAsync(
                "203.0.113.10",
                new[] { "not-a-cidr" },
                new[] { "*" },
                FilterPriority.Whitelist);

            Assert.IsFalse(result.NextCalled);
            Assert.AreEqual(StatusCodes.Status403Forbidden, result.StatusCode);
            Assert.Contains("403", result.ResponseBody);
        }

        [TestMethod]
        public async Task WildcardMatchesEveryAddress()
        {
            PipelineResult result = await ExecuteAsync(
                "203.0.113.10",
                new[] { "*" },
                Array.Empty<string>(),
                FilterPriority.Whitelist);

            Assert.IsTrue(result.NextCalled);
        }

        [TestMethod]
        [DataRow(FilterPriority.Whitelist, true)]
        [DataRow(FilterPriority.Blacklist, false)]
        public async Task ConflictUsesConfiguredPriority(FilterPriority priority, bool expectNext)
        {
            PipelineResult result = await ExecuteAsync(
                "203.0.113.10",
                new[] { "*" },
                new[] { "*" },
                priority);

            Assert.AreEqual(expectNext, result.NextCalled);
        }

        [TestMethod]
        public async Task ForwardedAddressDoesNotOverridePeerAddress()
        {
            PipelineResult result = await ExecuteAsync(
                "203.0.113.10",
                new[] { "198.51.100.0/24" },
                new[] { "*" },
                FilterPriority.Whitelist,
                context => context.Request.Headers["X-Forwarded-For"] = "198.51.100.7");

            Assert.IsFalse(result.NextCalled);
            Assert.AreEqual(StatusCodes.Status403Forbidden, result.StatusCode);
        }

        [TestMethod]
        public async Task LoopbackBypassesLists()
        {
            PipelineResult result = await ExecuteAsync(
                "127.0.0.1",
                Array.Empty<string>(),
                new[] { "*" },
                FilterPriority.Blacklist);

            Assert.IsTrue(result.NextCalled);
        }

        private static async Task<PipelineResult> ExecuteAsync(
            string remoteIp,
            IEnumerable<string> whitelist,
            IEnumerable<string> blacklist,
            FilterPriority priority,
            Action<HttpContext>? configureContext = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
            services.AddCidrFiltering(options =>
            {
                options.Whitelist = new List<string>(whitelist);
                options.Blacklist = new List<string>(blacklist);
                options.FilterPriority = priority;
                options.AllowBlacklistedRequests = false;
                options.AllowUnmatchedRequests = false;
                options.RecordBlacklistedRequests = false;
                options.RecordUnmatchedRequests = false;
                options.BlockStatusCode = StatusCodes.Status403Forbidden;
                options.LogLevelWhitelist = LogLevel.None;
                options.LogLevelBlacklist = LogLevel.None;
                options.LogLevelUnmatched = LogLevel.None;
            });

            using ServiceProvider provider = services.BuildServiceProvider();
            var app = new ApplicationBuilder(provider);
            bool nextCalled = false;
            app.UseCidrFiltering();
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
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            context.Response.Body = new MemoryStream();
            configureContext?.Invoke(context);

            await pipeline(context);

            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
            string responseBody = await reader.ReadToEndAsync();
            return new PipelineResult(nextCalled, context.Response.StatusCode, responseBody);
        }

        private sealed record PipelineResult(bool NextCalled, int StatusCode, string ResponseBody);
    }
}
