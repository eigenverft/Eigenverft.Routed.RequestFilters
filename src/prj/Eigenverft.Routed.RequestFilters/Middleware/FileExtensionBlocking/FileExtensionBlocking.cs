using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.FileExtensionBlocking
{
    /// <summary>
    /// Middleware that blocks requests by matching file extensions and/or path patterns on the request path.
    /// </summary>
    /// <remarks>
    /// Intended for suppressing noise requests (for example source map files) so they do not fall through to a reverse proxy.
    /// Place this middleware before your proxy mapping so blocked requests never reach upstream.
    /// </remarks>
    public sealed class FileExtensionBlocking
    {
        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<FileExtensionBlocking> _logger;
        private readonly IOptionsMonitor<FileExtensionBlockingOptions> _optionsMonitor;

        private volatile CompiledOptions _compiled;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileExtensionBlocking"/> class.
        /// </summary>
        /// <param name="nextMiddleware">The next middleware in the pipeline.</param>
        /// <param name="logger">The deferred logger instance.</param>
        /// <param name="optionsMonitor">The options monitor for <see cref="FileExtensionBlockingOptions"/>.</param>
        public FileExtensionBlocking(
            RequestDelegate nextMiddleware,
            IDeferredLogger<FileExtensionBlocking> logger,
            IOptionsMonitor<FileExtensionBlockingOptions> optionsMonitor)
        {
            _next = nextMiddleware ?? throw new ArgumentNullException(nameof(nextMiddleware));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));

            _compiled = Compile(_optionsMonitor.CurrentValue);

            _optionsMonitor.OnChange(o =>
            {
                _compiled = Compile(o);
                _logger.LogDebug("Configuration for {MiddlewareName} updated.", () => nameof(FileExtensionBlocking));
            });
        }

        /// <summary>
        /// Processes the current request and blocks it if the request path matches configured criteria.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            CompiledOptions compiled = _compiled;

            if (!compiled.Enabled)
            {
                await _next(context);
                return;
            }

            string? path = context.Request.Path.Value;
            if (string.IsNullOrEmpty(path))
            {
                await _next(context);
                return;
            }

            if (!path.StartsWith("/", StringComparison.Ordinal))
            {
                path = "/" + path;
            }

            if (!TryMatch(path, compiled, out string matchReason))
            {
                await _next(context);
                return;
            }

            if (compiled.LogLevel != LogLevel.None && _logger.IsEnabled(compiled.LogLevel))
            {
                _logger.Log(
                    compiled.LogLevel,
                    "FileExtensionBlocking blocked request. path={Path} reason={Reason} statusCode={StatusCode}.",
                    () => path!,
                    () => matchReason,
                    () => compiled.StatusCode);
            }

            context.Response.StatusCode = compiled.StatusCode;
        }

        private static bool TryMatch(string path, CompiledOptions compiled, out string matchReason)
        {
            // Extension matching
            if (compiled.Extensions.Length > 0)
            {
                for (int i = 0; i < compiled.Extensions.Length; i++)
                {
                    string ext = compiled.Extensions[i];
                    if (ext.Length == 0) continue;

                    if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    {
                        matchReason = "extension:" + ext;
                        return true;
                    }
                }
            }

            // Glob matching (converted to regex)
            for (int i = 0; i < compiled.GlobRegexes.Length; i++)
            {
                if (compiled.GlobRegexes[i].IsMatch(path))
                {
                    matchReason = "glob";
                    return true;
                }
            }

            // Regex matching
            for (int i = 0; i < compiled.PathRegexes.Length; i++)
            {
                if (compiled.PathRegexes[i].IsMatch(path))
                {
                    matchReason = "regex";
                    return true;
                }
            }

            matchReason = string.Empty;
            return false;
        }

        private static CompiledOptions Compile(FileExtensionBlockingOptions options)
        {
            bool enabled = options.Enabled;
            int statusCode = options.StatusCode;
            LogLevel logLevel = options.LogLevel;

            string[] extensions = NormalizeExtensions(options.Extensions);

            Regex[] globRegexes = CompileGlobPatterns(options.PathGlobPatterns);
            Regex[] pathRegexes = CompileRegexPatterns(options.PathRegexPatterns);

            return new CompiledOptions(enabled, extensions, globRegexes, pathRegexes, statusCode, logLevel);
        }

        private static string[] NormalizeExtensions(string[]? extensions)
        {
            if (extensions == null || extensions.Length == 0) return Array.Empty<string>();

            var tmp = new string[extensions.Length];
            int count = 0;

            for (int i = 0; i < extensions.Length; i++)
            {
                string ext = (extensions[i] ?? string.Empty).Trim();
                if (ext.Length == 0) continue;

                if (!ext.StartsWith(".", StringComparison.Ordinal))
                {
                    ext = "." + ext;
                }

                tmp[count++] = ext;
            }

            if (count == 0) return Array.Empty<string>();
            if (count == tmp.Length) return tmp;

            var result = new string[count];
            Array.Copy(tmp, result, count);
            return result;
        }

        private static Regex[] CompileRegexPatterns(string[]? patterns)
        {
            if (patterns == null || patterns.Length == 0) return Array.Empty<Regex>();

            var regexes = new Regex[patterns.Length];
            int count = 0;

            for (int i = 0; i < patterns.Length; i++)
            {
                string p = (patterns[i] ?? string.Empty).Trim();
                if (p.Length == 0) continue;

                // net6-net10 safe defaults
                regexes[count++] = new Regex(
                    p,
                    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            }

            if (count == 0) return Array.Empty<Regex>();
            if (count == regexes.Length) return regexes;

            var result = new Regex[count];
            Array.Copy(regexes, result, count);
            return result;
        }

        private static Regex[] CompileGlobPatterns(string[]? patterns)
        {
            if (patterns == null || patterns.Length == 0) return Array.Empty<Regex>();

            var regexes = new Regex[patterns.Length];
            int count = 0;

            for (int i = 0; i < patterns.Length; i++)
            {
                string glob = (patterns[i] ?? string.Empty).Trim();
                if (glob.Length == 0) continue;

                // Normalize to request-path style
                if (!glob.StartsWith("/", StringComparison.Ordinal))
                {
                    glob = "/" + glob;
                }

                string regexText = GlobToAnchoredRegex(glob);

                regexes[count++] = new Regex(
                    regexText,
                    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            }

            if (count == 0) return Array.Empty<Regex>();
            if (count == regexes.Length) return regexes;

            var result = new Regex[count];
            Array.Copy(regexes, result, count);
            return result;
        }

        /// <summary>
        /// Converts a glob pattern into an anchored regex.
        /// Supported tokens: * ? ** where ** crosses path separators.
        /// </summary>
        private static string GlobToAnchoredRegex(string glob)
        {
            // Anchor at start/end of the path.
            // Use \A \z to avoid multiline quirks.
            var sb = new System.Text.StringBuilder();
            sb.Append(@"\A");

            for (int i = 0; i < glob.Length; i++)
            {
                char c = glob[i];

                // ** => .*
                if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
                {
                    sb.Append(".*");
                    i++;
                    continue;
                }

                // * => any chars except '/'
                if (c == '*')
                {
                    sb.Append(@"[^/]*");
                    continue;
                }

                // ? => one char except '/'
                if (c == '?')
                {
                    sb.Append(@"[^/]");
                    continue;
                }

                // Escape regex meta chars
                if (c is '.' or '+' or '(' or ')' or '|' or '^' or '$' or '{' or '}' or '[' or ']' or '\\')
                {
                    sb.Append('\\');
                }

                sb.Append(c);
            }

            sb.Append(@"\z");
            return sb.ToString();
        }

        private sealed record CompiledOptions(
            bool Enabled,
            string[] Extensions,
            Regex[] GlobRegexes,
            Regex[] PathRegexes,
            int StatusCode,
            LogLevel LogLevel);
    }
}
