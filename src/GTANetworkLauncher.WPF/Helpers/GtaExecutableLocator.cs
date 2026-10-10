using System;
using System.Collections.Generic;
using System.IO;
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

			foreach (var fileName in Constants.GtaExecutableFileNames)
			{
				var candidate = Path.Combine(directory, fileName);

				if (File.Exists(candidate))
					return candidate;
			}

			return null;
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
		/// Covers the fixed and "Program Files" locations on every drive. The
		/// registry is queried separately by <see cref="GamePathProvider"/>, which
		/// knows how Rockstar records the install folder.
		/// </remarks>
		public static IEnumerable<string> CandidateDirectories()
		{
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var folder in Constants.GtaInstallFolderNames)
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