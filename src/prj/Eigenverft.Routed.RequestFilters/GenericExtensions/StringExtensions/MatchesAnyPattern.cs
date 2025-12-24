using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Eigenverft.Routed.RequestFilters.GenericExtensions.StringExtensions
{
    /// <summary>
    /// Provides extension methods for string operations, enhancing the built-in string manipulation capabilities.
    /// </summary>
    public static partial class StringExtensions
    {
        /// <summary>
        /// Checks if the input matches any of the provided patterns.
        /// </summary>
        /// <param name="input">The input string to be matched.</param>
        /// <param name="patterns">A list of patterns to match against the input.</param>
        /// <returns>True if any pattern matches the input; otherwise, false.</returns>
        public static bool MatchesAnyPattern(string? input, List<string>? patterns, bool ignoreCase = true)
        {
            if (patterns == null || patterns.Count == 0)
            {
                return false;
            }

            RegexOptions regexOptions = RegexOptions.None;
            if (ignoreCase)
            {
                regexOptions = RegexOptions.IgnoreCase;
            }

            foreach (var pattern in patterns)
            {
                if (IsRegExMatch(input, pattern, regexOptions))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if the input matches any of the provided patterns.
        /// </summary>
        /// <param name="input">The input string to be matched.</param>
        /// <param name="patterns">A list of patterns to match against the input.</param>
        /// <returns>True if any pattern matches the input; otherwise, false.</returns>
        public static bool MatchesAnyPattern(string? input, string[]? patterns, bool ignoreCase = true)
        {
            if (patterns == null || patterns.Length == 0)
            {
                return false;
            }

            RegexOptions regexOptions = RegexOptions.None;
            if (ignoreCase)
            {
                regexOptions = RegexOptions.IgnoreCase;
            }

            foreach (var pattern in patterns)
            {
                if (IsRegExMatch(input, pattern, regexOptions))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool MatchesAnyPattern(this string? input, IEnumerable<string>? patterns, bool ignoreCase = true)
        {
            return MatchesAnyPattern(input, patterns?.ToList(), ignoreCase);
        }

        public static bool MatchesAnyPattern(this string? input, string pattern, bool ignoreCase = true)
        {
            return input.MatchesAnyPattern(new List<string>() { pattern }, ignoreCase);
        }
    }
}