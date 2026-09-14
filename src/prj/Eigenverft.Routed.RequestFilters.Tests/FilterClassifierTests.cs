using System;

using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;

namespace Eigenverft.Routed.RequestFilters.Tests
{
    [TestClass]
    public sealed class FilterClassifierTests
    {
        [TestMethod]
        public void UnmatchedValueIsReportedAsUnmatched()
        {
            FilterMatchKind result = FilterClassifier.Classify(
                "observed",
                new[] { "allowed" },
                new[] { "blocked" },
                caseSensitive: false,
                FilterPriority.Blacklist);

            Assert.AreEqual(FilterMatchKind.Unmatched, result);
        }

        [TestMethod]
        public void ConflictUsesConfiguredPriority()
        {
            FilterMatchKind whitelistWins = FilterClassifier.Classify(
                "value",
                new[] { "*" },
                new[] { "*" },
                caseSensitive: false,
                FilterPriority.Whitelist);
            FilterMatchKind blacklistWins = FilterClassifier.Classify(
                "value",
                new[] { "*" },
                new[] { "*" },
                caseSensitive: false,
                FilterPriority.Blacklist);

            Assert.AreEqual(FilterMatchKind.Whitelist, whitelistWins);
            Assert.AreEqual(FilterMatchKind.Blacklist, blacklistWins);
        }

        [TestMethod]
        [DataRow("prefix-suffix", "prefix*suffix")]
        [DataRow("ac", "a?c")]
        [DataRow("abc", "a?c")]
        [DataRow("abc", "a#c")]
        public void WildcardTokensRetainTheirLegacyMeaning(string observed, string pattern)
        {
            FilterMatchKind result = FilterClassifier.Classify(
                observed,
                new[] { pattern },
                Array.Empty<string>(),
                caseSensitive: true,
                FilterPriority.Whitelist);

            Assert.AreEqual(FilterMatchKind.Whitelist, result);
        }

        [TestMethod]
        public void CaseSensitivityIsAppliedExplicitly()
        {
            FilterMatchKind insensitive = FilterClassifier.Classify(
                "VALUE",
                new[] { "value" },
                Array.Empty<string>(),
                caseSensitive: false,
                FilterPriority.Whitelist);
            FilterMatchKind sensitive = FilterClassifier.Classify(
                "VALUE",
                new[] { "value" },
                Array.Empty<string>(),
                caseSensitive: true,
                FilterPriority.Whitelist);

            Assert.AreEqual(FilterMatchKind.Whitelist, insensitive);
            Assert.AreEqual(FilterMatchKind.Unmatched, sensitive);
        }
    }
}
