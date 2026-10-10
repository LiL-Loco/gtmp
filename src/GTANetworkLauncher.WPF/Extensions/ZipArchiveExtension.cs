using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace GrandTheftMultiplayer.Launcher.Extensions
{
    public static class ZipArchiveExtensions
    {
        /// <summary>
        /// Extracts every entry of <paramref name="archive"/> below
        /// <paramref name="destinationDirectoryName"/>.
        /// </summary>
        /// <remarks>
        /// Entry names are attacker controlled data. A name such as
        /// "..\..\..\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\evil.exe"
        /// would otherwise resolve outside the destination. Because the application
        /// extracts with administrator rights and then executes the result, that is a
        /// direct privilege escalation, so every entry is resolved to an absolute path
        /// and rejected unless it stays inside the destination directory.
        /// </remarks>
        public static void ExtractToDirectory(this ZipArchive archive, string destinationDirectoryName, bool overwrite)
        {
            var destinationRoot = ResolveDestinationRoot(destinationDirectoryName);

            foreach (var compressedFile in archive.Entries)
            {
                var entryTargetPath = ResolveEntryTargetPath(destinationRoot, compressedFile.FullName);

                if (IsDirectoryEntry(compressedFile))
                {
                    Directory.CreateDirectory(entryTargetPath);
                    continue;
                }

                var directory = Path.GetDirectoryName(entryTargetPath);
                if (directory == null)
                    throw new InvalidOperationException($"Failed to get directory name of file {entryTargetPath}");

                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                compressedFile.ExtractToFile(entryTargetPath, overwrite);
            }
        }

        private static string ResolveDestinationRoot(string destinationDirectoryName)
        {
            var root = Path.GetFullPath(destinationDirectoryName);

            // Keep the trailing separator so that StartsWith cannot be satisfied by a
            // sibling directory whose name merely starts with the same characters.
            return root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;
        }

        private static string ResolveEntryTargetPath(string destinationRoot, string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName))
                throw new InvalidOperationException("Archive contains an entry without a name.");

            // Archives written on Windows may use backslashes, which are not directory
            // separators to the framework but are resolved by the OS on extraction.
            var normalizedEntryName = entryName.Replace('\\', Path.DirectorySeparatorChar);

            if (Path.IsPathRooted(normalizedEntryName))
                throw new InvalidOperationException($"Archive entry has a rooted path: {entryName}");

            // Reject before combining, so that ".." is never handed to the file system.
            if (normalizedEntryName.Split(Path.DirectorySeparatorChar).Any(segment => segment == ".."))
                throw new InvalidOperationException($"Archive entry escapes the destination directory: {entryName}");

            var entryTargetPath = Path.GetFullPath(Path.Combine(destinationRoot, normalizedEntryName));

            if (!entryTargetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Archive entry escapes the destination directory: {entryName}");

            return entryTargetPath;
        }

        private static bool IsDirectoryEntry(ZipArchiveEntry entry)
        {
            if (entry.Name.Length != 0) return false;

            var fullName = entry.FullName.Replace('\\', Path.DirectorySeparatorChar);
            return fullName.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal);
        }
    }
}