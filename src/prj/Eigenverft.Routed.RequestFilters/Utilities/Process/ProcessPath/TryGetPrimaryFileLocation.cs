using System;
using System.Reflection;

namespace Eigenverft.Routed.RequestFilters.Utilities.Process.ProcessPath
{
    /// <summary>
    /// Utility methods for resolving process and executable paths.
    /// </summary>
    public static partial class ProcessPath
    {
        /// <summary>
        /// Attempts to resolve the primary file path that represents the running application.
        /// </summary>
        /// <returns>
        /// The process path when available; otherwise the main module file path; otherwise the entry assembly location;
        /// or <c>null</c> if all attempts fail.
        /// </returns>
        public static string? TryGetPrimaryFileLocation()
        {
            try
            {
                var p = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(p))
                {
                    return p;
                }
            }
            catch
            {
                // ignored
            }

            try
            {
                var main = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(main))
                {
                    return main;
                }
            }
            catch
            {
                // ignored
            }

            try
            {
                var entry = Assembly.GetEntryAssembly()?.Location;
                if (!string.IsNullOrWhiteSpace(entry))
                {
                    return entry;
                }
            }
            catch
            {
                // ignored
            }

            return null;
        }
    }
}