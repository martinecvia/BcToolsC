#pragma warning disable
using System; // Keep for .NET 4.6
using System.Collections.Generic; // Keep for .NET 4.6

namespace BcToolsC.Helpers
{
    public static class PathHelper
    {
        // Copyright (c) 2014, Yves Goergen, http://unclassified.software/source/getrelativepath
        // Copying and distribution of this file, with or without modification, are permitted provided the
        // notice and this notice are preserved. This file is offered as-is, without any warranty.
        /// <summary>
        /// Create a relative path from one path to another. Paths will be resolved before calculating the difference.
        /// Default path comparison for the active platform will be used (OrdinalIgnoreCase for Windows or Mac, Ordinal for Unix).
        /// </summary>
        /// <param name="relativeTo">The source path the output should be relative to. This path is always considered to be a directory.</param>
        /// <param name="lsPath">The destination path.</param>
        /// <param name="throwOnDifferentRoot">If true, an exception is thrown for different roots, otherwise the source path is returned unchanged.</param>
        /// <returns>The relative path or <paramref name="lsPath"/> if the paths don't share the same root.</returns>
        public static string GetRelativePath(string relativeTo, string lsPath,
            StringComparison @case = StringComparison.OrdinalIgnoreCase, bool throwOnDifferentRoot = false)
        {
            if (string.IsNullOrEmpty(lsPath)) throw new ArgumentNullException(nameof(lsPath));
            if (string.IsNullOrEmpty(relativeTo)) return lsPath;
            if (!System.IO.Path.IsPathRooted(relativeTo))
                throw new ArgumentException($"{nameof(relativeTo)} is not a rooted path.");
            if (!System.IO.Path.IsPathRooted(lsPath))
                throw new ArgumentException($"{nameof(lsPath)} is not a rooted path.");
            // Normalizace cesty
            relativeTo = TrimEndingDirectorySeparator(relativeTo);
            lsPath = TrimEndingDirectorySeparator(lsPath);
            // Sdílejí cesty stejnej základ?
            string relativeToRoot = System.IO.Path.GetPathRoot(relativeTo) ?? string.Empty;
            string lsPathRoot = System.IO.Path.GetPathRoot(lsPath) ?? string.Empty;
            if (!string.Equals(lsPathRoot, relativeToRoot, @case)
                || string.IsNullOrEmpty(relativeToRoot) || string.IsNullOrEmpty(lsPathRoot))
            {
                if (throwOnDifferentRoot)
                    throw new InvalidOperationException("Both paths do not share the same root.");
                else return lsPath;
            }
            // Šmykyšmyk
            lsPath = lsPath.Substring(lsPathRoot.Length);
            relativeTo = relativeTo.Substring(relativeToRoot.Length);
            string[] lsPathParts = lsPath.Split('\\');
            string[] relativeToParts = relativeTo.Split('\\');
            int commonCount;
            for (commonCount = 0; commonCount < lsPathParts.Length && commonCount < relativeToParts.Length &&
                 string.Equals(lsPathParts[commonCount], relativeToParts[commonCount], @case);
                 commonCount++)
            { }
            List<string> newPath = new List<string>();
            // Nahrazení společné části relativní cestou ".."
            for (int i = commonCount; i < relativeToParts.Length; i++)
                newPath.Add("..");
            for (int j = commonCount; j < lsPathParts.Length; j++)
                newPath.Add(lsPathParts[j]);
            return newPath.Count == 0 ? "." : string.Join("\\", newPath);
        }

        public static string GetFullPath(string lsPath)
            => System.IO.Path.GetFullPath(lsPath);

        public static string GetFullPath(string relativeTo, string lsPath)
        {
            if (string.IsNullOrEmpty(relativeTo)) return GetFullPath(lsPath);
            if (string.IsNullOrEmpty(lsPath)) throw new ArgumentNullException(nameof(lsPath));
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(relativeTo, lsPath));
        }
        #region PRIVATE
        private static bool EndsInDirectorySeparator(string lsPath) => !string.IsNullOrEmpty(lsPath) && IsDirectorySeparator(lsPath[lsPath.Length - 1]);
        private static bool IsRoot(string lsPath) => lsPath.Length == GetRootLength(lsPath);
        private static bool IsDirectorySeparator(char c) => c == System.IO.Path.DirectorySeparatorChar || c == System.IO.Path.AltDirectorySeparatorChar;
        private static bool IsValidDriveChar(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        private static bool IsDevice(string lsPath)
        {
            return IsExtended(lsPath) || (
                lsPath.Length >= 4
                && IsDirectorySeparator(lsPath[0])
                && IsDirectorySeparator(lsPath[1])
                && (lsPath[2] == '.' || lsPath[2] == '?')
                && IsDirectorySeparator(lsPath[3])
            );
        }

        private static string TrimEndingDirectorySeparator(string lsPath) =>
            EndsInDirectorySeparator(lsPath) && !IsRoot(lsPath) 
                ? lsPath.Substring(0, lsPath.Length - 1) 
                : lsPath;

        private static bool IsExtended(string lsPath)
        {
            return lsPath.Length >= 4 && lsPath[0] == '\\'
                && (lsPath[1] == '\\' || lsPath[1] == '?') && lsPath[2] == '?' && lsPath[3] == '\\';
        }

        private static bool IsDeviceUNC(string lsPath)
        {
            return lsPath.Length >= 8
                && IsDevice(lsPath)
                && IsDirectorySeparator(lsPath[7])
                && lsPath[4] == 'U' && lsPath[5] == 'N' && lsPath[6] == 'C';
        }

        private static int GetRootLength(string lsPath)
        {
            int pathLength = lsPath.Length; int i = 0;
            bool asDevice = IsDevice(lsPath);
            bool asDeviceUNC = asDevice && IsDeviceUNC(lsPath);
            if ((!asDevice || asDeviceUNC) && pathLength > 0 && IsDirectorySeparator(lsPath[0]))
            {
                if (asDeviceUNC || (pathLength > 1 && IsDirectorySeparator(lsPath[1])))
                {
                    i = asDeviceUNC ? 8 : 2;
                    int n = 2;
                    while (i < pathLength && (!IsDirectorySeparator(lsPath[i]) || --n > 0))
                        i++;
                }
                else { i = 1; }
            }
            else if (asDevice)
            {
                i = 4;
                while (i < pathLength && !IsDirectorySeparator(lsPath[i]))
                    i++;
                if (i < pathLength && i > 4 && IsDirectorySeparator(lsPath[i]))
                    i++;
            }
            else if (pathLength >= 2
                && lsPath[1] == System.IO.Path.VolumeSeparatorChar
                && IsValidDriveChar(lsPath[0]))
            {
                i = 2;
                if (pathLength > 2 && IsDirectorySeparator(lsPath[2]))
                    i++;
            }
            return i;
        }
        #endregion
    }
}