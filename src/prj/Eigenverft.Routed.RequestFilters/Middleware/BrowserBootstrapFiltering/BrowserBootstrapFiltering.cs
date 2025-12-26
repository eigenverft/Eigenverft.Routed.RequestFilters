using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.GenericExtensions.StringExtensions;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.BrowserBootstrapFiltering
{
    /// <summary>
    /// Middleware that enforces a “browser bootstrap” (JavaScript + cookie write)
    /// for configured HTML entry paths.
    /// </summary>
    /// <remarks>
    /// Reviewer note:
    /// - Only applies to GET/HEAD requests whose path matches <see cref="BrowserBootstrapFilteringOptions.HtmlProtectedBootstrapScopePathPatterns"/>.
    /// - If the bootstrap cookie is missing/invalid, a loading HTML page (200) is served (no server redirects).
    /// - A browser with JavaScript and cookies enabled will set the cookie and reload.
    /// - curl and other non-JS clients remain on the loading page.
    /// </remarks>
    public sealed class BrowserBootstrapFiltering
    {
        private const string TokenVersion = "v2";

        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<BrowserBootstrapFiltering> _logger;
        private readonly IOptionsMonitor<BrowserBootstrapFilteringOptions> _optionsMonitor;
        private readonly IDataProtector _protector;

        /// <summary>
        /// Initializes a new instance of the <see cref="BrowserBootstrapFiltering"/> class.
        /// </summary>
        public BrowserBootstrapFiltering(
            RequestDelegate nextMiddleware,
            IDeferredLogger<BrowserBootstrapFiltering> logger,
            IOptionsMonitor<BrowserBootstrapFilteringOptions> optionsMonitor,
            IDataProtectionProvider dataProtectionProvider)
        {
            _next = nextMiddleware ?? throw new ArgumentNullException(nameof(nextMiddleware));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _protector = (dataProtectionProvider ?? throw new ArgumentNullException(nameof(dataProtectionProvider)))
                .CreateProtector("Eigenverft.BrowserBootstrapFiltering.CookieToken.v2");

            _optionsMonitor.OnChange(_ => _logger.LogDebug(
                "Configuration for {MiddlewareName} updated.",
                () => nameof(BrowserBootstrapFiltering)));
        }

        /// <summary>
        /// Processes the current request and either forwards it or serves bootstrap HTML for in-scope paths.
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var req = context.Request;

            // Only meaningful for document-like navigations.
            if (!HttpMethods.IsGet(req.Method) && !HttpMethods.IsHead(req.Method))
            {
                await _next(context);
                return;
            }

            BrowserBootstrapFilteringOptions options = _optionsMonitor.CurrentValue;

            string path = req.Path.HasValue ? req.Path.Value! : string.Empty;
            if (!IsInScope(path, options))
            {
                await _next(context);
                return;
            }

            string trace = context.TraceIdentifier;

            LogScopeIfEnabled(options, trace, path);

            if (TryValidateBootstrapCookie(req, options, out _))
            {
                LogIfEnabled(
                    options.LogLevelBootstrapPassed,
                    "{Middleware} action={Action} observed={Observed} trace={Trace}",
                    () => nameof(BrowserBootstrapFiltering),
                    () => "BootstrapPassed",
                    () => path,
                    () => trace);

                await _next(context);
                return;
            }

            LogIfEnabled(
                options.LogLevelBootstrapServed,
                "{Middleware} action={Action} observed={Observed} trace={Trace}",
                () => nameof(BrowserBootstrapFiltering),
                () => "BootstrapServed",
                () => path,
                () => trace);

            if (HttpMethods.IsHead(req.Method))
            {
                ApplyBootstrapResponseHeaders(context.Response);
                context.Response.StatusCode = StatusCodes.Status200OK;
                return;
            }

            string cookieToken = MintCookieToken(req, options);
            await WriteBootstrapHtmlAsync(context, options, cookieToken);
        }

        private static bool IsInScope(string path, BrowserBootstrapFilteringOptions options)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string[] patterns = options.HtmlProtectedBootstrapScopePathPatterns?.ToArray() ?? Array.Empty<string>();
            if (patterns.Length == 0)
            {
                return false;
            }

            // Reviewer note: This slimmed version always matches case-insensitively.
            return path.MatchesAnyPattern(patterns, ignoreCase: true);
        }

        private void LogScopeIfEnabled(BrowserBootstrapFilteringOptions options, string traceIdentifier, string observedPath)
        {
            LogLevel level = options.LogLevelBootstrapScopePaths;
            if (!_logger.IsEnabled(level))
            {
                return;
            }

            _logger.Log(
                level,
                "{Middleware} action={Action} observed={Observed} trace={Trace}",
                () => nameof(BrowserBootstrapFiltering),
                () => "BootstrapScope",
                () => observedPath,
                () => traceIdentifier);
        }

        private bool TryValidateBootstrapCookie(HttpRequest request, BrowserBootstrapFilteringOptions options, out string reason)
        {
            reason = string.Empty;

            if (!request.Cookies.TryGetValue(options.CookieName, out string? protectedToken))
            {
                reason = "CookieMissing";
                return false;
            }

            if (string.IsNullOrWhiteSpace(protectedToken))
            {
                reason = "CookieEmpty";
                return false;
            }

            string payload;
            try
            {
                payload = _protector.Unprotect(protectedToken);
            }
            catch
            {
                reason = "CookieUnprotectFailed";
                return false;
            }

            // payload: v2|expUtcTicks|uaHashB64
            string[] parts = payload.Split('|');
            if (parts.Length != 3)
            {
                reason = "CookieFormat";
                return false;
            }

            if (!string.Equals(parts[0], TokenVersion, StringComparison.Ordinal))
            {
                reason = "CookieVersion";
                return false;
            }

            if (!long.TryParse(parts[1], out long expTicks))
            {
                reason = "CookieExpiryParse";
                return false;
            }

            if (DateTimeOffset.UtcNow >= new DateTimeOffset(expTicks, TimeSpan.Zero))
            {
                reason = "CookieExpired";
                return false;
            }

            string expectedUaHash = parts[2] ?? string.Empty;
            string actualUaHash = ComputeUserAgentHashB64(request);

            if (!string.Equals(expectedUaHash, actualUaHash, StringComparison.Ordinal))
            {
                reason = "CookieUserAgentMismatch";
                return false;
            }

            return true;
        }

        private string MintCookieToken(HttpRequest request, BrowserBootstrapFilteringOptions options)
        {
            DateTimeOffset expUtc = DateTimeOffset.UtcNow.Add(options.CookieMaxAge);
            string uaHash = ComputeUserAgentHashB64(request);

            string payload = string.Concat(TokenVersion, "|", expUtc.Ticks.ToString(), "|", uaHash);
            return _protector.Protect(payload);
        }

        private static string ComputeUserAgentHashB64(HttpRequest request)
        {
            string ua = request.Headers.UserAgent.ToString().Trim();
            byte[] bytes = Encoding.UTF8.GetBytes(ua);
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToBase64String(hash);
        }

        private static Task WriteBootstrapHtmlAsync(HttpContext context, BrowserBootstrapFilteringOptions options, string protectedCookieToken)
        {
            ApplyBootstrapResponseHeaders(context.Response);

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/html; charset=utf-8";

            string html = BrowserBootstrapHtml.Build(
                cookieName: options.CookieName,
                cookieValue: protectedCookieToken,
                cookieMaxAge: options.CookieMaxAge,
                traceIdentifier: context.TraceIdentifier);

            return context.Response.WriteAsync(html);
        }

        private static void ApplyBootstrapResponseHeaders(HttpResponse response)
        {
            response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            response.Headers.Pragma = "no-cache";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["Referrer-Policy"] = "no-referrer";

            response.Headers["Content-Security-Policy"] =
                "default-src 'none'; " +
                "script-src 'unsafe-inline'; " +
                "base-uri 'none'; " +
                "form-action 'none'; " +
                "frame-ancestors 'none'";
        }

        private void LogIfEnabled(LogLevel level, string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_logger.IsEnabled(level))
            {
                return;
            }

            _logger.Log(level, messageTemplate, argumentFactories);
        }
    }

    internal static class BrowserBootstrapHtml
    {
        // Browser-side single-attempt guard key (kept here so this type is self-contained).
        private const string ClientAttemptKey = "evf.bootstrap.attempted";

        public static string Build(string cookieName, string cookieValue, TimeSpan cookieMaxAge, string traceIdentifier)
        {
            static string JsEscape(string s) => (s ?? string.Empty)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("'", "\\'", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);

            int maxAgeSeconds = (int)Math.Clamp(cookieMaxAge.TotalSeconds, 60, int.MaxValue);

            string name = JsEscape(cookieName);
            string value = JsEscape(cookieValue);
            string trace = JsEscape(traceIdentifier);
            string attemptKey = JsEscape(ClientAttemptKey);

            var sb = new StringBuilder(2200);

            sb.AppendLine("<!doctype html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("  <meta charset=\"utf-8\" />");
            sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
            sb.AppendLine("  <meta name=\"robots\" content=\"noindex, nofollow\" />");
            sb.AppendLine("  <title>Loading…</title>");
            sb.AppendLine("  <style>");
            sb.AppendLine("    body{font-family:system-ui,-apple-system,Segoe UI,Roboto,Ubuntu,Cantarell,Noto Sans,sans-serif;margin:2rem;}");
            sb.AppendLine("    .muted{opacity:.75}");
            sb.AppendLine("    #fail{display:none;margin-top:1rem;}");
            sb.AppendLine("    code{font-family:ui-monospace,SFMono-Regular,Menlo,Monaco,Consolas,monospace;}");
            sb.AppendLine("  </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("  <h1 id=\"status\">Loading…</h1>");
            sb.AppendLine("  <p class=\"muted\">This page verifies that your browser supports JavaScript and cookies.</p>");
            sb.AppendLine("  <noscript>");
            sb.AppendLine("    <p><strong>JavaScript is required.</strong></p>");
            sb.AppendLine("    <p class=\"muted\">Trace Id: <code>" + trace + "</code></p>");
            sb.AppendLine("  </noscript>");
            sb.AppendLine("  <div id=\"fail\">");
            sb.AppendLine("    <p><strong>Unable to continue.</strong></p>");
            sb.AppendLine("    <p class=\"muted\">Please enable cookies for this site and reload. Trace Id: <code>" + trace + "</code></p>");
            sb.AppendLine("  </div>");
            sb.AppendLine("  <script>");
            sb.AppendLine("  (function(){");
            sb.AppendLine("    function hasCookieExact(n, v){");
            sb.AppendLine("      var all = document.cookie ? document.cookie.split('; ') : [];");
            sb.AppendLine("      for (var i=0;i<all.length;i++){");
            sb.AppendLine("        var p = all[i];");
            sb.AppendLine("        if (p.indexOf(n + '=') === 0) return p.substring(n.length + 1) === v;");
            sb.AppendLine("      }");
            sb.AppendLine("      return false;");
            sb.AppendLine("    }");
            sb.AppendLine("    function fail(){");
            sb.AppendLine("      try { document.getElementById('status').textContent = 'Loading failed'; } catch(e){}");
            sb.AppendLine("      try { document.getElementById('fail').style.display = 'block'; } catch(e){}");
            sb.AppendLine("    }");
            sb.AppendLine("    var name = '" + name + "';");
            sb.AppendLine("    var value = '" + value + "';");
            sb.AppendLine("    if (hasCookieExact(name, value)) return;");
            sb.AppendLine("    var attempted = false;");
            sb.AppendLine("    try { attempted = (sessionStorage.getItem('" + attemptKey + "') === '1'); } catch(e){}");
            sb.AppendLine("    if (attempted) { fail(); return; }");
            sb.AppendLine("    try { sessionStorage.setItem('" + attemptKey + "', '1'); } catch(e){}");
            sb.AppendLine("    try {");
            sb.AppendLine("      var secure = (location.protocol === 'https:') ? '; Secure' : '';");
            sb.AppendLine("      document.cookie = name + '=' + value + '; Path=/; Max-Age=" + maxAgeSeconds + "; SameSite=Lax' + secure;");
            sb.AppendLine("    } catch (e) { }");
            sb.AppendLine("    if (hasCookieExact(name, value)) {");
            sb.AppendLine("      try { sessionStorage.removeItem('" + attemptKey + "'); } catch(e){}");
            sb.AppendLine("      location.replace(location.href);");
            sb.AppendLine("    } else {");
            sb.AppendLine("      fail();");
            sb.AppendLine("    }");
            sb.AppendLine("  })();");
            sb.AppendLine("  </script>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }
    }
}
