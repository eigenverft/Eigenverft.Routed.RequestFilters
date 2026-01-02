using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Eigenverft.Routed.RequestFilters.Middleware.RequestSignatureFiltering
{
    /// <summary>
    /// Helper that constructs a single request signature string from an <see cref="HttpContext"/>.
    /// </summary>
    /// <remarks>
    /// Reviewer note: The builder uses an explicit variant switch so that future signature formats can be added
    /// without changing middleware control flow.
    /// </remarks>
    public static class RequestSignatureBuilder
    {
        /// <summary>
        /// Creates the request signature string for the provided context using the specified variant.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <param name="options">The options controlling normalization and future builder evolution.</param>
        /// <param name="variant">The signature variant.</param>
        /// <returns>A single string containing context properties and headers.</returns>
        public static string CreateRequestSignature(HttpContext context, RequestSignatureFilteringOptions options, RequestSignatureSchema variant)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (options == null) throw new ArgumentNullException(nameof(options));

            switch (variant)
            {
                case RequestSignatureSchema.Version1:
                    return CreateRequestSignatureV1(context, options);

                default:
                    // Reviewer note: Default to V1 to avoid runtime breakage when new enum values are introduced.
                    return CreateRequestSignatureV1(context, options);
            }
        }

        /// <summary>
        /// Creates the Version1 request signature string.
        /// </summary>
        /// <remarks>
        /// Reviewer note: V1 includes:
        /// <list type="bullet">
        /// <item><description><c>HTTP.Method</c></description></item>
        /// <item><description><c>HTTP.Protocol</c></description></item>
        /// <item><description><c>HTTP.Scheme</c></description></item>
        /// <item><description>All request headers (sorted, case-insensitive)</description></item>
        /// </list>
        /// Formatting uses stable separators designed to be easy to place in JSON.
        /// </remarks>
        private static string CreateRequestSignatureV1(HttpContext context, RequestSignatureFilteringOptions options)
        {
            const string EntrySeparator = " | ";
            const string KeyValueSeparator = "=";

            var sb = new StringBuilder(capacity: 512);

            void Append(string key, string value)
            {
                // Reviewer note: Always protect against delimiter collisions and control characters.
                // This ensures that a header value cannot accidentally create "fake entries".
                key = SanitizeV1(key, options, EntrySeparator, KeyValueSeparator, isKey: true);
                value = SanitizeV1(value, options, EntrySeparator, KeyValueSeparator, isKey: false);

                if (sb.Length != 0) sb.Append(EntrySeparator);
                sb.Append(key);
                sb.Append(KeyValueSeparator);
                sb.Append(value);
            }

            Append("HTTP.Method", context.Request.Method ?? string.Empty);
            Append("HTTP.Protocol", context.Request.Protocol ?? string.Empty);
            Append("HTTP.Scheme", context.Request.Scheme ?? string.Empty);

            IEnumerable<KeyValuePair<string, StringValues>> headers =
                context.Request.Headers.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var header in headers)
            {
                string headerName = header.Key ?? string.Empty;
                if (headerName.Length == 0) continue;

                string headerValue = header.Value.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(headerValue)) continue;

                Append(headerName, headerValue);
            }

            return sb.ToString();
        }

        private static string SanitizeV1(string input, RequestSignatureFilteringOptions options, string entrySeparator, string keyValueSeparator, bool isKey)
        {
            input ??= string.Empty;

            // 1) Configurable sanitization (existing behavior).
            string output = Sanitize(input, options);

            // 2) Remove / normalize control characters that may appear in raw header strings.
            output = output
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Replace("\t", " ", StringComparison.Ordinal);

            // 3) Prevent delimiter collision: entry separator must never appear inside key/value payload.
            // Use the configured replacement so normalization remains consistent across all tokens.
            string replacement = options.SignatureSanitizeReplacement ?? string.Empty;

            if (!string.IsNullOrEmpty(entrySeparator))
            {
                output = output.Replace(entrySeparator, replacement, StringComparison.Ordinal);
            }

            // 4) Defensive: header names should never contain "=", but protect anyway.
            // Do NOT replace '=' in values because it is common and meaningful (boundary=..., name=..., etc.).
            if (isKey && !string.IsNullOrEmpty(keyValueSeparator))
            {
                output = output.Replace(keyValueSeparator, replacement, StringComparison.Ordinal);
            }

            return output.Trim();
        }

        private static string Sanitize(string input, RequestSignatureFilteringOptions options)
        {
            input ??= string.Empty;

            var tokens = options.SignatureSanitizeTokens;
            if (tokens == null || tokens.Length == 0)
            {
                return input;
            }

            string replacement = options.SignatureSanitizeReplacement ?? string.Empty;

            string output = input;
            foreach (string token in tokens)
            {
                if (string.IsNullOrEmpty(token)) continue;
                output = output.Replace(token, replacement, StringComparison.Ordinal);
            }

            return output;
        }
    }
}
