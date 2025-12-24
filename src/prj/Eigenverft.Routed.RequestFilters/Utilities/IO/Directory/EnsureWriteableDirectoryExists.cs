using System;
using System.IO;

namespace Eigenverft.Routed.RequestFilters.Utilities.IO.Directory
{
    public static partial class Directory
    {
        /// <summary>
        /// Ensures a writable directory exists at <paramref name="baseDirectory"/>/<paramref name="subDirectory"/>.
        /// </summary>
        /// <param name="baseDirectory">The base directory to test and/or create under.</param>
        /// <param name="subDirectory">The subdirectory name to ensure exists.</param>
        /// <param name="throwIfFails">Whether to throw if creation fails under a writable base directory.</param>
        /// <returns>A <see cref="DirectoryInfo"/> if the resulting directory exists and is writable; otherwise <c>null</c>.</returns>
        public static DirectoryInfo? EnsureWriteableDirectoryExists(string baseDirectory, string subDirectory, bool throwIfFails)
        {
            var path = Path.Combine(baseDirectory, subDirectory);
            var dirInfo = new DirectoryInfo(path);

            if (dirInfo.Exists)
            {
                return IsWritableDirectory(dirInfo.FullName) ? dirInfo : null;
            }

            if (!IsWritableDirectory(baseDirectory))
            {
                return null;
            }

            try
            {
                System.IO.Directory.CreateDirectory(path);
                var created = new DirectoryInfo(path);
                return IsWritableDirectory(created.FullName) ? created : null;
            }
            catch (Exception ex)
            {
                if (throwIfFails)
                {
                    throw new IOException($"Failed to create directory: {path}", ex);
                }

                return null;
            }
        }
    }
}