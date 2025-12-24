using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Eigenverft.Routed.RequestFilters.Utilities.Process.ProcessPath;

namespace Eigenverft.Routed.RequestFilters.Utilities.Storage.AppDirectoryLayout
{
    /// <summary>
    /// Resolves a writable per-executable root folder and ensures required directories exist under it.
    /// </summary>
    public static class AppDirectoryLayoutResolver
    {
        /// <summary>
        /// Resolves the directory layout for the current executable.
        /// </summary>
        /// <param name="directoryMap">Semantic key to relative path mapping under the resolved root.</param>
        /// <param name="candidateBaseDirectories">Candidate base directories to try in order. If <c>null</c>, a sensible default list is used.</param>
        /// <param name="throwIfFails">If <c>true</c>, throws when directory creation fails under a writable candidate base directory.</param>
        /// <returns>A resolved <see cref="AppDirectoryLayout"/>.</returns>
        public static AppDirectoryLayout Resolve(IReadOnlyDictionary<string, string> directoryMap, string[]? candidateBaseDirectories = null, bool throwIfFails = true)
        {
            if (directoryMap is null) throw new ArgumentNullException(nameof(directoryMap));
            if (directoryMap.Count == 0) throw new ArgumentException("directoryMap must not be empty.", nameof(directoryMap));

            var exePath = ProcessPath.TryGetPrimaryFileLocation()
                ?? throw new IOException("Unable to determine primary application file location.");

            var exeName = Path.GetFileNameWithoutExtension(exePath);
            if (string.IsNullOrWhiteSpace(exeName))
            {
                throw new IOException("Unable to determine executable name from primary file location.");
            }

            var normalizedMap = NormalizeAndValidateMap(directoryMap);

            candidateBaseDirectories ??= BuildDefaultCandidates(exePath);

            string? rootPath = null;

            foreach (var baseDir in candidateBaseDirectories.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                var rootDir = IO.Directory.Directory.EnsureWriteableDirectoryExists(baseDir!, exeName, throwIfFails);
                if (rootDir is null)
                {
                    continue;
                }

                var ok = true;
                foreach (var rel in normalizedMap.Values.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var ensured = IO.Directory.Directory.EnsureWriteableDirectoryExists(rootDir.FullName, rel, throwIfFails);
                    if (ensured is null)
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    rootPath = rootDir.FullName;
                    break;
                }
            }

            if (rootPath is null)
            {
                throw new IOException("No writable base directory found for application data.");
            }

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in normalizedMap)
            {
                var full = Path.GetFullPath(Path.Combine(rootPath, kvp.Value));
                dict[kvp.Key] = full;
            }

            return new AppDirectoryLayout(rootPath, dict);
        }

        private static Dictionary<string, string> NormalizeAndValidateMap(IReadOnlyDictionary<string, string> input)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in input)
            {
                var key = kvp.Key;
                var relPath = kvp.Value;

                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new ArgumentException("directoryMap contains an empty key.", nameof(input));
                }

                if (string.IsNullOrWhiteSpace(relPath))
                {
                    throw new ArgumentException($"directoryMap['{key}'] is null/empty.", nameof(input));
                }

                if (Path.IsPathRooted(relPath))
                {
                    throw new ArgumentException($"directoryMap['{key}'] must be a relative path, but was rooted: '{relPath}'.", nameof(input));
                }

                var cleaned = relPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                if (cleaned.Split(Path.DirectorySeparatorChar).Any(seg => string.Equals(seg, "..", StringComparison.Ordinal)))
                {
                    throw new ArgumentException($"directoryMap['{key}'] must not contain '..' traversal segments.", nameof(input));
                }

                result[key] = cleaned;
            }

            return result;
        }

        private static string[] BuildDefaultCandidates(string exePath)
        {
            var exeDir = Path.GetDirectoryName(exePath);

            return new[]
                {
                    exeDir,
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    Path.GetTempPath(),
                }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()!;
        }
    }
}
