using System.IO;

namespace Eigenverft.Routed.RequestFilters.Utilities.Process.ProcessPath
{
    /// <summary>
    /// Utility methods for resolving process and executable paths.
    /// </summary>
    public static partial class ProcessPath
    {
        /// <summary>
        /// Attempts to resolve the executable name without extension for the running process.
        /// </summary>
        /// <returns>The executable name without extension or <c>null</c>.</returns>
        public static string? TryGetExecutableNameWithoutExtension()
        {
            var primary = TryGetPrimaryFileLocation();
            if (string.IsNullOrWhiteSpace(primary))
            {
                return null;
            }

            var exeName = Path.GetFileNameWithoutExtension(primary);
            return string.IsNullOrWhiteSpace(exeName) ? null : exeName;
        }
    }
}