using Eigenverft.Routed.RequestFilters.GenericExtensions.StringExtensions;

namespace Eigenverft.Routed.RequestFilters.Middleware.Abstractions
{
    /// <summary>
    /// Defines the resolution strategy when both whitelist and blacklist patterns match.
    /// </summary>
    public enum FilterPriority
    {
        /// <summary>
        /// A conflicting match is treated as allowed.
        /// </summary>
        Whitelist,

        /// <summary>
        /// A conflicting match is treated as forbidden.
        /// </summary>
        Blacklist
    }

    /// <summary>
    /// Represents the result of matching a value against whitelist and blacklist patterns.
    /// </summary>
    public enum FilterMatchKind
    {
        /// <summary>
        /// The value matches the whitelist and is explicitly allowed.
        /// </summary>
        Whitelist,

        /// <summary>
        /// The value matches the blacklist and is explicitly forbidden.
        /// </summary>
        Blacklist,

        /// <summary>
        /// The value matches neither whitelist nor blacklist.
        /// </summary>
        Unmatched
    }

    /// <summary>
    /// Provides helper methods for classifying values against whitelist and blacklist patterns.
    /// </summary>
    /// <remarks>
    /// This helper is intended for use by middleware components that need to classify values such as
    /// HTTP protocol strings or host names based on configured whitelist and blacklist patterns.
    /// </remarks>
    /// <example>
    /// Example usage with options:
    /// <code>
    /// var match = FilterClassifier.Classify(
    ///     httpContext.Request.Protocol,
    ///     options.Whitelist,
    ///     options.Blacklist,
    ///     options.CaseSensitive,
    ///     options.FilterPriority);
    /// </code>
    /// </example>
    public static class FilterClassifier
    {
        /// <summary>
        /// Classifies a value against whitelist and blacklist pattern arrays.
        /// </summary>
        /// <param name="value">
        /// The value to classify, for example an HTTP protocol string or host name.
        /// </param>
        /// <param name="whitelist">
        /// The whitelist patterns. May be null, in which case no whitelist patterns are applied.
        /// </param>
        /// <param name="blacklist">
        /// The blacklist patterns. May be null, in which case no blacklist patterns are applied.
        /// </param>
        /// <param name="caseSensitive">
        /// When true, pattern matching is case sensitive. When false, matching ignores case.
        /// </param>
        /// <param name="filterPriority">
        /// The resolution strategy to use when the value matches both whitelist and blacklist patterns.
        /// </param>
        /// <returns>
        /// A value from <see cref="FilterMatchKind"/> that represents the classification result.
        /// </returns>
        /// <remarks>
        /// The classification logic follows these rules:
        /// <list type="number">
        /// <item>
        /// <description>
        /// If the value matches neither whitelist nor blacklist, the result is <see cref="FilterMatchKind.Unmatched"/>.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// If the value matches only the whitelist, the result is <see cref="FilterMatchKind.Whitelist"/>.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// If the value matches only the blacklist, the result is <see cref="FilterMatchKind.Blacklist"/>.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// If the value matches both lists, the result is determined by <paramref name="filterPriority"/>.
        /// </description>
        /// </item>
        /// </list>
        /// </remarks>
        public static FilterMatchKind Classify(string value, string[]? whitelist, string[]? blacklist, bool caseSensitive, FilterPriority filterPriority)
        {
            value ??= string.Empty;

            bool inWhitelist = value.MatchesAnyPattern(whitelist, ignoreCase: !caseSensitive);
            bool inBlacklist = value.MatchesAnyPattern(blacklist, ignoreCase: !caseSensitive);

            if (!inWhitelist && !inBlacklist)
            {
                return FilterMatchKind.Unmatched;
            }

            if (inWhitelist && !inBlacklist)
            {
                return FilterMatchKind.Whitelist;
            }

            if (!inWhitelist && inBlacklist)
            {
                return FilterMatchKind.Blacklist;
            }

            // Konflikt: inWhitelist && inBlacklist
            return filterPriority == FilterPriority.Whitelist ? FilterMatchKind.Whitelist : FilterMatchKind.Blacklist;
        }
    }
}