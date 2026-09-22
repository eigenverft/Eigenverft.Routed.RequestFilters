using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Eigenverft.Routed.RequestFilters.Middleware.Abstractions
{
    internal static class FilterPatternMatcher
    {
        internal static bool MatchesAnyPattern(
            this string? input,
            IEnumerable<string>? patterns,
            bool ignoreCase = true)
        {
            if (patterns == null)
            {
                return false;
            }

            RegexOptions regexOptions = ignoreCase
                ? RegexOptions.IgnoreCase
                : RegexOptions.None;

            foreach (string pattern in patterns)
            {
                if (IsPatternMatch(input, pattern, regexOptions))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPatternMatch(
            string? input,
            string? pattern,
            RegexOptions regexOptions)
        {
            if (input == null || pattern == null)
            {
                return input == null && pattern == null;
            }

            // '*' => 0..n, '?' => 0..1, '#' => exactly 1
            int minimumInputLength = pattern.Replace("*", "").Replace("?", "").Length;

            if (input.Length < minimumInputLength)
            {
                return false;
            }

            string regexPattern =
                "^" + Regex.Escape(pattern)
                    .Replace(@"\*", ".*")
                    .Replace(@"\?", ".?")
                    .Replace(@"\#", ".")
                + "$";

            return Regex.IsMatch(input, regexPattern, regexOptions);
        }
    }
}
