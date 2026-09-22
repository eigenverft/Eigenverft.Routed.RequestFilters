using System.IO;
using System.Net;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Middleware.FilteringEvaluationGate;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Tests
{
    [TestClass]
    public sealed class FilteringEvaluationGateTests
    {
        [TestMethod]
        public async Task BlockingDecisionShortCircuitsWithoutPublishingDownstreamMarker()
        {
            GateResult result = await ExecuteAsync(
                FilteringDecision.Block,
                options =>
                {
                    options.AllowBlockedRequests = false;
                    options.EmitMarkedAsBlockedByEvaluator = true;
                    options.BlockStatusCode = StatusCodes.Status451UnavailableForLegalReasons;
                });

            Assert.IsFalse(result.NextCalled);
            Assert.AreEqual(StatusCodes.Status451UnavailableForLegalReasons, result.StatusCode);
            Assert.IsFalse(result.MarkerExists);
            Assert.IsFalse(result.MarkedAsBlocked);
            Assert.AreEqual("203.0.113.17", result.ObservedRemoteIpAddress);
        }

        [TestMethod]
        public async Task PassThroughPublishesTypedBlockedMarkerForDownstreamMiddleware()
        {
            GateResult result = await ExecuteAsync(
                FilteringDecision.Block,
                options =>
                {
                    options.AllowBlockedRequests = true;
                    options.EmitMarkedAsBlockedByEvaluator = true;
                });

            Assert.IsTrue(result.NextCalled);
            Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
            Assert.IsTrue(result.MarkerExists);
            Assert.IsTrue(result.MarkedAsBlocked);
        }

        [TestMethod]
        public async Task AllowedDecisionContinuesWithoutPublishingMarker()
        {
            GateResult result = await ExecuteAsync(
                FilteringDecision.Allow,
                options => options.EmitMarkedAsBlockedByEvaluator = true);

            Assert.IsTrue(result.NextCalled);
            Assert.IsFalse(result.MarkerExists);
            Assert.IsFalse(result.MarkedAsBlocked);
        }

        private static async Task<GateResult> ExecuteAsync(
            FilteringDecision decision,
            System.Action<FilteringEvaluationGateOptions> configure)
        {
            var evaluator = new FixedFilteringEvaluator(decision);
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
            services.AddFilteringEvaluationGate(configure);
            services.AddSingleton<IFilteringEvaluationService>(evaluator);

            using ServiceProvider provider = services.BuildServiceProvider();
            var app = new ApplicationBuilder(provider);
            bool nextCalled = false;
            bool markerExists = false;
            bool markedAsBlocked = false;

            app.UseFilteringEvaluationGate();
            app.Run(context =>
            {
                nextCalled = true;
                markerExists = context.TryGetEvaluatorWouldBlock(out markedAsBlocked);
                return Task.CompletedTask;
            });

            RequestDelegate pipeline = app.Build();
            var context = new DefaultHttpContext
            {
                RequestServices = provider,
            };
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.17");
            context.Response.Body = new MemoryStream();

            await pipeline(context);

            return new GateResult(
                nextCalled,
                context.Response.StatusCode,
                markerExists,
                markedAsBlocked,
                evaluator.ObservedRemoteIpAddress);
        }

        private sealed class FixedFilteringEvaluator : IFilteringEvaluationService
        {
            private readonly FilteringDecision _decision;

            internal FixedFilteringEvaluator(FilteringDecision decision)
            {
                _decision = decision;
            }

            internal string ObservedRemoteIpAddress { get; private set; } = string.Empty;

            public FilteringEvaluationResult Evaluate(string remoteIpAddress)
            {
                ObservedRemoteIpAddress = remoteIpAddress;
                return new FilteringEvaluationResult
                {
                    Decision = _decision,
                    EvaluationReason = "Test decision",
                };
            }
        }

        private sealed record GateResult(
            bool NextCalled,
            int StatusCode,
            bool MarkerExists,
            bool MarkedAsBlocked,
            string ObservedRemoteIpAddress);
    }
}
