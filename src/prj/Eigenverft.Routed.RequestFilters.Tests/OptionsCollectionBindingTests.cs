using System;
using System.IO;
using System.Text;

using Eigenverft.Routed.RequestFilters.Middleware.AcceptLanguageFiltering;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation.FilteringEvaluators;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvaluation.FilteringEvaluators.SourceAndMatchKindWeighted;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Tests
{
    [TestClass]
    public sealed class OptionsCollectionBindingTests
    {
        [TestMethod]
        public void MissingListConfigurationKeepsCodeDefaults()
        {
            IConfigurationRoot configuration = BuildJson("""
                {
                  "AcceptLanguageFilteringOptions": {
                    "Enabled": true
                  }
                }
                """);

            AcceptLanguageFilteringOptions options = ResolveAcceptLanguageOptions(configuration);

            Assert.AreSequenceEqual(new[] { "*" }, options.Whitelist);
            Assert.AreSequenceEqual(new[] { "", "*zh-CN*", "*zh-*", "*-CN*" }, options.Blacklist);
        }

        [TestMethod]
        public void ConfiguredListsReplaceCodeDefaults()
        {
            IConfigurationRoot configuration = BuildJson("""
                {
                  "AcceptLanguageFilteringOptions": {
                    "Whitelist": ["de-DE"],
                    "Blacklist": ["fr-FR"]
                  }
                }
                """);

            AcceptLanguageFilteringOptions options = ResolveAcceptLanguageOptions(configuration);

            Assert.AreSequenceEqual(new[] { "de-DE" }, options.Whitelist);
            Assert.AreSequenceEqual(new[] { "fr-FR" }, options.Blacklist);
        }

        [TestMethod]
        public void EmptyListsKeepCodeDefaults()
        {
            IConfigurationRoot configuration = BuildJson("""
                {
                  "AcceptLanguageFilteringOptions": {
                    "Whitelist": [],
                    "Blacklist": []
                  }
                }
                """);

            AcceptLanguageFilteringOptions options = ResolveAcceptLanguageOptions(configuration);

            Assert.AreSequenceEqual(new[] { "*" }, options.Whitelist);
            Assert.AreSequenceEqual(new[] { "", "*zh-CN*", "*zh-*", "*-CN*" }, options.Blacklist);
        }

        [TestMethod]
        public void ExplicitConfigurationSourceReloadReappliesEmptyListPolicy()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Eigenverft.Routed.RequestFilters.Tests", Guid.NewGuid().ToString("N"));
            string fileName = "appsettings.json";
            string filePath = Path.Combine(directory, fileName);
            Directory.CreateDirectory(directory);

            try
            {
                File.WriteAllText(filePath, """
                    {
                      "AcceptLanguageFilteringOptions": {
                        "Whitelist": ["configured"]
                      }
                    }
                    """);

                IConfigurationRoot explicitConfiguration = new ConfigurationBuilder()
                    .SetBasePath(directory)
                    .AddJsonFile(fileName, optional: false, reloadOnChange: false)
                    .Build();
                IConfigurationRoot configurationFromDi = BuildJson("""
                    {
                      "AcceptLanguageFilteringOptions": {
                        "Whitelist": ["from-di"]
                      }
                    }
                    """);

                var services = new ServiceCollection();
                services.AddSingleton<IConfiguration>(configurationFromDi);
                services.AddAcceptLanguageFiltering(explicitConfiguration);

                using ServiceProvider provider = services.BuildServiceProvider();
                IOptionsMonitor<AcceptLanguageFilteringOptions> monitor =
                    provider.GetRequiredService<IOptionsMonitor<AcceptLanguageFilteringOptions>>();

                Assert.AreSequenceEqual(new[] { "configured" }, monitor.CurrentValue.Whitelist);

                File.WriteAllText(filePath, """
                    {
                      "AcceptLanguageFilteringOptions": {
                        "Whitelist": []
                      }
                    }
                    """);
                explicitConfiguration.Reload();

                Assert.AreSequenceEqual(new[] { "*" }, monitor.CurrentValue.Whitelist);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [TestMethod]
        public void EvaluatorDictionaryUsesTheSameReplacementPolicy()
        {
            IConfigurationRoot configured = BuildJson("""
                {
                  "SourceAndMatchKindWeightedFilteringEvaluatorOptions": {
                    "SourceFactors": {
                      "OnlyConfiguredSource": 7
                    }
                  }
                }
                """);
            IConfigurationRoot empty = BuildJson("""
                {
                  "SourceAndMatchKindWeightedFilteringEvaluatorOptions": {
                    "SourceFactors": {}
                  }
                }
                """);

            SourceAndMatchKindWeightedFilteringEvaluatorOptions configuredOptions = ResolveEvaluatorOptions(configured);
            SourceAndMatchKindWeightedFilteringEvaluatorOptions emptyOptions = ResolveEvaluatorOptions(empty);

            Assert.HasCount(1, configuredOptions.SourceFactors);
            Assert.AreEqual(7, configuredOptions.SourceFactors["OnlyConfiguredSource"]);
            Assert.IsFalse(configuredOptions.SourceFactors.ContainsKey("HostNameFiltering"));
            Assert.AreEqual(1, emptyOptions.SourceFactors["HostNameFiltering"]);
            Assert.AreEqual(1, emptyOptions.SourceFactors["TlsProtocolFiltering"]);
        }

        private static AcceptLanguageFilteringOptions ResolveAcceptLanguageOptions(IConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddSingleton(configuration);
            services.AddAcceptLanguageFiltering();

            using ServiceProvider provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IOptions<AcceptLanguageFilteringOptions>>().Value;
        }

        private static SourceAndMatchKindWeightedFilteringEvaluatorOptions ResolveEvaluatorOptions(IConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddSingleton(configuration);
            services.AddFilteringEvaluator(FilteringEvaluatorKind.SourceAndMatchKindWeighted);

            using ServiceProvider provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IOptions<SourceAndMatchKindWeightedFilteringEvaluatorOptions>>().Value;
        }

        private static IConfigurationRoot BuildJson(string json)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return new ConfigurationBuilder()
                .AddJsonStream(stream)
                .Build();
        }
    }
}
