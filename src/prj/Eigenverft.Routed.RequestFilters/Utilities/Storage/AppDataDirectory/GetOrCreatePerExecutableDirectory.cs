using System;
using System.IO;
using System.Linq;

using Eigenverft.Routed.RequestFilters.Utilities.Process.ProcessPath;

namespace Eigenverft.Routed.RequestFilters.Utilities.Storage.AppDataDirectory
{
    /// <summary>
    /// Resolves an application-specific writable directory using candidate base directories and a per-executable folder name.
    /// </summary>
    public static partial class AppDataDirectory
    {
        /// <summary>
        /// Locates or creates a writable root directory named after the executable and ensures required subdirectories.
        /// </summary>
        /// <param name="candidateBaseDirectories">Candidate base directories to try in order. If <c>null</c>, defaults are used (exe dir first).</param>
        /// <param name="subdirectoriesToEnsure">Subdirectories that must exist (and be writable) under the resulting root directory.</param>
        /// <param name="throwIfFails">If <c>true</c>, throws when creation fails under a writable candidate base directory.</param>
        /// <returns>The writable root directory path.</returns>
        public static string GetOrCreatePerExecutableDirectory(string[]? candidateBaseDirectories, string[] subdirectoriesToEnsure, bool throwIfFails)
        {
            if (subdirectoriesToEnsure is null) throw new ArgumentNullException(nameof(subdirectoriesToEnsure));
            if (subdirectoriesToEnsure.Length == 0) throw new ArgumentException("At least one subdirectory must be provided.", nameof(subdirectoriesToEnsure));

            var exeName = ProcessPath.TryGetExecutableNameWithoutExtension() ?? throw new IOException("Unable to determine executable name.");

            candidateBaseDirectories ??= BuildDefaultCandidates();

            foreach (var baseDirectory in candidateBaseDirectories)
            {
                if (string.IsNullOrWhiteSpace(baseDirectory))
                {
                    continue;
                }

                var root = IO.Directory.Directory.EnsureWriteableDirectoryExists(baseDirectory, exeName, throwIfFails);
                if (root is null)
                {
                    continue;
                }

                foreach (var sub in subdirectoriesToEnsure)
                {
                    if (string.IsNullOrWhiteSpace(sub))
                    {
                        if (throwIfFails)
                        {
                            throw new IOException("A required subdirectory name was null/empty.");
                        }

                        root = null;
                        break;
                    }

                    var ensured = IO.Directory.Directory.EnsureWriteableDirectoryExists(root.FullName, sub, throwIfFails);
                    if (ensured is null)
                    {
                        root = null;
                        break;
                    }
                }

                if (root is not null)
                {
                    return root.FullName;
                }
            }

            throw new IOException("No writable base directory found for application data.");
        }

        private static string[] BuildDefaultCandidates()
        {
            var exeDir = ProcessPath.TryGetExecutableDirectory();

            return new[]
                {
                    exeDir,
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    Path.GetTempPath(),
                }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToArray()!;
        }
    }
}
