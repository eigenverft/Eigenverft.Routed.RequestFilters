using System.Text.RegularExpressions;

namespace Eigenverft.Routed.RequestFilters.GenericExtensions.StringExtensions
{
    /// <summary>
    /// Provides extension methods for string operations, enhancing the built-in string manipulation capabilities.
    /// </summary>
    public static partial class StringExtensions
    {
        /// <summary>
        /// Attempts to match the given input against the specified pattern using regular expressions.
        /// Returns true if the input matches the pattern, false otherwise.
        /// </summary>
        /// <param name="input">The input string to be matched.</param>
        /// <param name="pattern">The pattern string, where '*' matches any number of characters, '?' matches zero or one, and '#' matches exactly one.</param>
        /// <returns>True if the input matches the pattern; otherwise, false.</returns>
        private static bool IsRegExMatch(string? input, string? pattern, RegexOptions regexOptions = RegexOptions.IgnoreCase)
        {
            if (input == null || pattern == null)
            {
                return input == null && pattern == null;
            }

            // '*' => 0..n, '?' => 0..1, '#' => exactly 1
            int minInputLength = pattern.Replace("*", "").Replace("?", "").Length;

            if (input.Length < minInputLength)
            {
                return false;
            }

            var regexPattern =
                "^" + Regex.Escape(pattern)
                    .Replace(@"\*", ".*")
                    .Replace(@"\?", ".?")
                    .Replace(@"\#", ".")
                + "$";

            return Regex.IsMatch(input, regexPattern, regexOptions);
        }

    }
}