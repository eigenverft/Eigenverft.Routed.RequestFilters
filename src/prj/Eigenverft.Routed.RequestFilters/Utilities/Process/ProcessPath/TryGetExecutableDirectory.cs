using System.IO;

namespace Eigenverft.Routed.RequestFilters.Utilities.Process.ProcessPath
{
    /// <summary>
    /// Utility methods for resolving process and executable paths.
    /// </summary>
    public static partial class ProcessPath
    {
        /// <summary>
        /// Attempts to resolve the executable directory for the running process.
        /// </summary>
        /// <returns>The directory path or <c>null</c>.</returns>
        public static string? TryGetExecutableDirectory()
        {
            var primary = TryGetPrimaryFileLocation();
            return string.IsNullOrWhiteSpace(primary) ? null : Path.GetDirectoryName(primary);
        }
    }
}