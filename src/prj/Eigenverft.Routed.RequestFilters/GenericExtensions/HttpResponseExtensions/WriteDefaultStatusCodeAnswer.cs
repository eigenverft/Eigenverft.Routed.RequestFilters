using System.Net;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Utilities.HttpStatusCodeDescriptions;

using Microsoft.AspNetCore.Http;

namespace Eigenverft.Routed.RequestFilters.GenericExtensions.HttpResponseExtensions
{
    public static partial class HttpResponseExtensions
    {
        /// <summary>
        /// Adds security headers to the HTTP response.
        /// </summary>
        /// <param name="response">The HTTP response.</param>
        public static async Task WriteDefaultStatusCodeAnswer(this HttpResponse response, int StatusCode)
        {
            response.StatusCode = StatusCode;
            response.ContentType = "text/html";
            string responseMessage = $"<html><body><h1><p>{StatusCode} - {HttpStatusCodeDescriptions.GetStatusCodeDescription(StatusCode)}</p></h1></body></html>";
            await response.WriteAsync(responseMessage);
        }

        /// <summary>
        /// Writes a styled HTML response using the Eigenverft landing-page layout,
        /// including light/dark mode support and a short explanation text.
        /// </summary>
        /// <param name="response">The HTTP response.</param>
        /// <param name="statusCode">The HTTP status code to write.</param>
        /// <returns>A task that represents the asynchronous write operation.</returns>
        public static async Task WriteDefaultStatusCodeAnswerEx(this HttpResponse response, int statusCode)
        {
            response.StatusCode = statusCode;
            response.ContentType = "text/html; charset=utf-8";
            
            var description = HttpStatusCodeDescriptions.GetStatusCodeDescription(statusCode);

            string html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"" />
    <title>Eigenverft</title>
    <style>
        :root {{
            color-scheme: light dark;
            --bg-color: #ffffff;
            --text-color: #111111;
        }}

        @media (prefers-color-scheme: dark) {{
            :root {{
                --bg-color: #111111;
                --text-color: #f5f5f5;
            }}
        }}

        body {{
            margin: 0;
            min-height: 100vh;
            background: var(--bg-color);
            color: var(--text-color);
            font-family: system-ui, -apple-system, BlinkMacSystemFont, ""Segoe UI"", sans-serif;
            -webkit-user-select: none;
            -moz-user-select: none;
            -ms-user-select: none;
            user-select: none;
            padding: 2rem;
        }}

        .shell,
        .brand-title,
        p {{
            -webkit-user-select: none;
            -moz-user-select: none;
            -ms-user-select: none;
            user-select: none;
        }}

        .shell {{
            max-width: 720px;
        }}

        .brand-title {{
            font-size: 2.25rem;
            font-weight: 700;
            letter-spacing: 0.12em;
            text-transform: uppercase;
            margin-bottom: 1rem;
        }}

        .status {{
            margin: 0 0 0.75rem 0;
            font-size: 1.05rem;
            font-weight: 500;
        }}

        p {{
            margin: 0;
            font-size: 0.95rem;
        }}
    </style>
</head>
<body>
    <div class=""shell"">
        <p class=""status"">{statusCode} – {description}</p>
    </div>
</body>
</html>";

            await response.WriteAsync(html);
        }


    }
}