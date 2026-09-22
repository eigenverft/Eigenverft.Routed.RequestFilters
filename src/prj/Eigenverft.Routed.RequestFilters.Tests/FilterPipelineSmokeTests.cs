using System.IO;
using System.Net;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Middleware.AcceptLanguageFiltering;
using Eigenverft.Routed.RequestFilters.Middleware.BrowserBootstrapFiltering;
using Eigenverft.Routed.RequestFilters.Middleware.DevelopmentUnlocker;
using Eigenverft.Routed.RequestFilters.Middleware.FileExtensionBlocking;
using Eigenverft.Routed.RequestFilters.Middleware.FilteringEvaluationGate;
using Eigenverft.Routed.RequestFilters.Middleware.HostNameFiltering;
using Eigenverft.Routed.RequestFilters.Middleware.HttpMethodFiltering;
using Eigenverft.Routed.RequestFilters.Middleware.HttpProtocolFiltering;
using Eigenverft.Routed.RequestFilters.Middleware.PathDepthFiltering;
using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressFiltering;
using Eigenverft.Routed.RequestFilters.Middleware.RequestSignatureFiltering;
using Eigenverft.Routed.RequestFilters.Middleware.RequestUrlFiltering;
using Eigenverft.Routed.RequestFilters.Middleware.TlsProtocolFiltering;
using Eigenverft.Routed.RequestFilters.Middleware.UriSegmentFiltering;
using Eigenverft.Routed.RequestFilters.Middleware.UserAgentFiltering;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation.FilteringEvaluators;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Eigenverft.Routed.RequestFilters.Tests
{
    [TestClass]
    public sealed class FilterPipelineSmokeTests
    {
        [TestMethod]
        public Task AcceptLanguagePipelineActivates() => AssertPipelinePassesAsync(
            services => services.AddAcceptLanguageFiltering(),
            app => app.UseAcceptLanguageFiltering());

        [TestMethod]
        public Task BrowserBootstrapPipelineActivates() => AssertPipelinePassesAsync(
            services => services.AddBrowserBootstrapFiltering(),
            app => app.UseBrowserBootstrapFiltering());

        [TestMethod]
        public Task DevelopmentUnlockerPipelineActivates() => AssertPipelinePassesAsync(
            services => services.AddDevelopmentUnlocker(),
            app => app.UseDevelopmentUnlocker());

        [TestMethod]
        public Task FileExtensionBlockingPipelineActivates() => AssertPipelinePassesAsync(
            services => services.AddFileExtensionBlocking(),
            app => app.UseFileExtensionBlocking());

        [TestMethod]
        public Task FilteringEvaluationGatePipelineActivates() => AssertPipelinePassesAsync(
            services =>
            {
                services.AddFilteringEvaluationGate();
                services.AddFilteringEvaluator(FilteringEvaluatorKind.NullFiltering);
            },
            app => app.UseFilteringEvaluationGate());

        [TestMethod]
        public Task HostNamePipelineActivates() => AssertPipelinePassesAsync(
            services => services.AddHostNameFiltering(),
            app => app.UseHostNameFiltering());

        [TestMethod]
        public Task HttpMethodPipelineActivates() => AssertPipelinePassesAsync(
            services => services.AddHttpMethodFiltering(),
            app => app.UseHttpMethodFiltering());

        [TestMethod]
        public Task HttpProtocolPipelineActivates() => AssertPipelinePassesAsync(
            services => services.AddHttpProtocolFiltering(),
            app => app.UseHttpProtocolFiltering());

        [TestMethod]
        public Task PathDepthPipelineActivates() => AssertPipelinePassesAsync(
            services => services.AddPathDepthFiltering(),
            app => app.UsePathDepthFiltering());

        [TestMethod]
        public Task RemoteIpAddressPipelineActivates() => AssertPipelinePassesAsync(
            services => services.AddRemoteIpAddressFiltering(),
            app => app.UseRemoteIpAddressFiltering());

        [TestMethod]
        public Task RequestSignaturePipelineActivates() => AssertPipelinePassesAsync(
            services => services.AddRequestSignatureFiltering(),
            app => app.UseRequestSignatureFiltering());

        [TestMethod]
        public Task RequestUrlPipelineActivates() => AssertPipelinePassesAsync(
            services => services.AddRequestUrlFiltering(),
            app => app.UseRequestUrlFiltering());

        [TestMethod]
        public Task TlsProtocolPipelineActivates() => AssertPipelinePassesAsync(
            services => services.AddTlsProtocolFiltering(),
            app => app.UseTlsProtocolFiltering());

        [TestMethod]
        public Task UriSegmentPipelineActivates() => AssertPipelinePassesAsync(
            services => services.AddUriSegmentFiltering(),
            app => app.UseUriSegmentFiltering());

        [TestMethod]
        public Task UserAgentPipelineActivates() => AssertPipelinePassesAsync(
            services => services.AddUserAgentFiltering(),
            app => app.UseUserAgentFiltering());

        private static async Task AssertPipelinePassesAsync(
            System.Action<IServiceCollection> register,
            System.Action<IApplicationBuilder> use)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
            register(services);

            using ServiceProvider provider = services.BuildServiceProvider();
            var app = new ApplicationBuilder(provider);
            bool nextCalled = false;
            use(app);
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
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Method = HttpMethods.Get;
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("localhost");
            context.Request.Path = "/smoke";
            context.Request.Protocol = "HTTP/1.1";
            context.Request.Headers.AcceptLanguage = "en-US";
            context.Request.Headers.UserAgent = "RequestFilters-SmokeTest/1.0";
            context.Response.Body = new MemoryStream();

            await pipeline(context);

            Assert.IsTrue(nextCalled);
        }
    }
}
