using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GrandTheftMultiplayer.Launcher.Services;

namespace GrandTheftMultiplayer.Launcher.Helpers
{
	/// <summary>
	/// Locates the GTA V executable across both Rockstar editions.
	/// </summary>
	/// <remarks>
	/// The launcher used to test for a hardcoded "GTA5.exe" in five separate
	/// places: registry discovery, the saved-settings check, the browse dialog
	/// filter, the launch step and the version check. The Enhanced edition ships
	/// "GTA5_Enhanced.exe" in a folder named "Grand Theft Auto V Enhanced", so
	/// every one of those checks rejected a perfectly good installation.
	/// <para>
	/// Centralising it here means adding a future edition touches one list in
	/// <see cref="Constants.GtaExecutableFileNames"/> instead of five call sites
	/// that can drift apart.
	/// </para>
	/// </remarks>
	public static class GtaExecutableLocator
	{
		/// <summary>
		/// The executable names in the order they should be probed.
		/// </summary>
		/// <remarks>
		/// Driven by <see cref="Constants.PreferredGtaEdition"/> so that switching
		/// editions is a one-line change. With the default "legacy", GTA5.exe wins
		/// whenever both editions are installed.
		/// </remarks>
		private static IEnumerable<string> PreferredExecutableNames()
		{
			var preferEnhanced = string.Equals(
				Constants.PreferredGtaEdition, "enhanced", StringComparison.OrdinalIgnoreCase);

			if (preferEnhanced)
			{
				for (var i = Constants.GtaExecutableFileNames.Length - 1; i >= 0; i--)
					yield return Constants.GtaExecutableFileNames[i];
			}
			else
			{
				foreach (var name in Constants.GtaExecutableFileNames)
					yield return name;
			}
		}

		/// <summary>
		/// Returns the GTA V executable inside <paramref name="directory"/>, or null.
		/// </summary>
		/// <remarks>
		/// Checks only the directory it is given. Callers that need registry or
		/// filesystem discovery use <see cref="FindInInstallDirectory"/> or
		/// <see cref="CandidateDirectories"/> first.
		/// </remarks>
		public static string FindExecutable(string directory)
		{
			if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
				return null;

			foreach (var fileName in PreferredExecutableNames())
			{
				var candidate = Path.Combine(directory, fileName);

				if (File.Exists(candidate))
					return candidate;
			}

			return null;
		}

		/// <summary>
		/// True when the directory holds the executable of the preferred edition.
		/// </summary>
		/// <remarks>
		/// Used to notice that a saved path points at the other edition. Without it a
		/// settings.xml written for the Enhanced edition keeps winning even after
		/// the legacy edition is installed.
		/// </remarks>
		public static bool MatchesPreferredEdition(string directory)
		{
			var preferred = FindExecutable(directory);

			if (preferred == null) return false;

			return string.Equals(
				Path.GetFileName(preferred),
				PreferredExecutableNames().First(),
				StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>True when the directory holds a GTA V executable of either edition.</summary>
		public static bool IsGtaDirectory(string directory)
		{
			return FindExecutable(directory) != null;
		}

		/// <summary>True when the path points at a GTA V executable of either edition.</summary>
		public static bool IsGtaExecutable(string executablePath)
		{
			if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
				return false;

			var fileName = Path.GetFileName(executablePath);

			foreach (var known in Constants.GtaExecutableFileNames)
			{
				if (string.Equals(fileName, known, StringComparison.OrdinalIgnoreCase))
					return true;
			}

			return false;
		}

		/// <summary>
		/// Installation directories to probe, most specific first.
		/// </summary>
		/// <remarks>
		/// Covers the fixed and "Program Files" locations on every drive. Rockstar
		/// appends the edition to the folder name ("Grand Theft Auto V Legacy"), so
		/// the known names are matched by prefix rather than compared exactly.
		/// The registry is queried separately by <see cref="GamePathProvider"/>,
		/// which knows how Rockstar records the install folder.
		/// </remarks>
		public static IEnumerable<string> CandidateDirectories()
		{
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			var folders = Constants.GtaInstallFolderNames.ToList();

			if (string.Equals(Constants.PreferredGtaEdition, "enhanced", StringComparison.OrdinalIgnoreCase))
			{
				folders.Reverse();
			}

			foreach (var folder in folders)
			{
				foreach (var root in DriveRoots())
				{
					string path;

					try
					{
						path = Path.Combine(root, @"Program Files\Rockstar Games", folder);
					}
					catch (ArgumentException)
					{
						continue;
					}

					if (seen.Add(path))
						yield return path;

					try
					{
						path = Path.Combine(root, @"Program Files (x86)\Rockstar Games", folder);
					}
					catch (ArgumentException)
					{
						continue;
					}

					if (seen.Add(path))
						yield return path;
				}
			}

			// Prefix sweep. Rockstar picks the folder name itself, so a hardcoded
			// list is never complete - the legacy build arrived here as
			// "Grand Theft Auto V Legacy". Anything under a Rockstar Games directory
			// that actually contains a GTA V executable is a candidate, and matching
			// on the executable rather than the name keeps this edition-agnostic.
			foreach (var directory in ScanRockstarDirectories())
			{
				if (seen.Add(directory))
					yield return directory;
			}
		}

		/// <summary>
		/// Every directory under "Rockstar Games" on any drive that holds a GTA V
		/// executable.
		/// </summary>
		private static IEnumerable<string> ScanRockstarDirectories()
		{
			foreach (var root in DriveRoots())
			{
				foreach (var relative in new[]
				{
					@"Program Files\Rockstar Games",
					@"Program Files (x86)\Rockstar Games"
				})
				{
					string rockstarRoot;

					try
					{
						rockstarRoot = Path.Combine(root, relative);
					}
					catch (ArgumentException)
					{
						continue;
					}

					if (!Directory.Exists(rockstarRoot)) continue;

					string[] subdirectories;

					try
					{
						subdirectories = Directory.GetDirectories(rockstarRoot);
					}
					catch (IOException)
					{
						continue;
					}
					catch (UnauthorizedAccessException)
					{
						continue;
					}

					foreach (var directory in subdirectories)
					{
						if (FindExecutable(directory) != null)
							yield return directory;
					}
				}
			}
		}

		/// <summary>
		/// All existing drives, including removable ones, skipping drives that are
		/// not ready yet.
		/// </summary>
		private static IEnumerable<string> DriveRoots()
		{
			foreach (var drive in DriveInfo.GetDrives())
			{
				if (!drive.IsReady)
					continue;

				yield return drive.RootDirectory.FullName;
			}
		}
	}
}