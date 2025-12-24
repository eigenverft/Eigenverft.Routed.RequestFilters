using System.IO;

namespace Eigenverft.Routed.RequestFilters.Utilities.IO.Directory
{
    /// <summary>
    /// Utility methods for checking writability and ensuring directories exist.
    /// </summary>
    public static partial class Directory
    {
        /// <summary>
        /// Determines whether the process can create and delete a file in the specified directory.
        /// </summary>
        /// <param name="directory">Directory to test.</param>
        /// <returns><c>true</c> if writable; otherwise <c>false</c>.</returns>
        public static bool IsWritableDirectory(string directory)
        {
            try
            {
                using var _ = File.Create(Path.Combine(directory, Path.GetRandomFileName()), bufferSize: 1, options: FileOptions.DeleteOnClose);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}