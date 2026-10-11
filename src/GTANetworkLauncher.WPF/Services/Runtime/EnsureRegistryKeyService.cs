using System;
using System.IO;
using GrandTheftMultiplayer.Launcher.Models.Logger;
using GrandTheftMultiplayer.Launcher.Services.Interfaces;
using Microsoft.Win32;

namespace GrandTheftMultiplayer.Launcher.Services.Runtime
{
	/// <summary>
	/// Makes sure the Rockstar install folder registry entries match the game path the
	/// launcher is configured with.
	/// </summary>
	/// <remarks>
	/// The launcher discovers the game through these keys (see GamePathProvider) and
	/// starts the game through the Steam protocol or Social Club, both of which resolve
	/// the install location from the same entries. If the key points somewhere else -
	/// a leftover from a previous install, or a second copy of the game - the launcher
	/// verifies one installation and starts another.
	///
	/// The keys live under HKLM and therefore need administrator rights, which this
	/// process has. Only missing or mismatching values are written; an existing correct
	/// value is left untouched.
	/// </remarks>
	public class EnsureRegistryKeyService : IEnsureRegistryKeyService
	{
		private const string SteamKeyPath = @"SOFTWARE\WOW6432Node\Rockstar Games\GTAV";
		private const string SteamInstallValueName = "InstallFolderSteam";

		private const string RockstarKeyPath = @"SOFTWARE\Wow6432Node\Rockstar Games\Grand Theft Auto V";
		private const string RockstarInstallValueName = "InstallFolder";

		// The Enhanced edition registers under its own key. Writing the Enhanced
		// folder into the legacy key was wrong in two ways: it mislabels the
		// installation for anything reading that key, and it makes
		// GamePathProvider find the Enhanced copy first, so a machine with both
		// editions can never settle on the legacy one.
		private const string RockstarEnhancedKeyPath = @"SOFTWARE\Wow6432Node\Rockstar Games\Grand Theft Auto V Enhanced";

		private const string SteamGameSubFolder = "GTAV";

		private readonly ILogger _logger;
		private readonly IGamePathProvider _gamePathProvider;

		public EnsureRegistryKeyService(ILogger logger, IGamePathProvider gamePathProvider)
		{
			_logger = logger;
			_gamePathProvider = gamePathProvider;
		}

		public void TryEnsureRegistryKey()
		{
			// Called from the launch chain. A failure here must not abort the launch,
			// it must be visible in the log.
			try
			{
				var gamePath = _gamePathProvider.GetGta5DirectoryPath();

				if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
				{
					_logger.Write("Cannot ensure registry key, no valid game path available.", LogMessageSeverity.Warning);
					return;
				}

				if (IsSteamInstallation(gamePath))
					EnsureSteamRegistryValue(gamePath);
				else
					EnsureRockstarRegistryValue(gamePath);
			}
			catch (Exception e)
			{
				_logger.Write(e);
			}
		}

		private void EnsureSteamRegistryValue(string gamePath)
		{
			// GamePathProvider strips the "\GTAV" suffix again when it reads this value.
			var expectedPath = Path.Combine(gamePath, SteamGameSubFolder);

			EnsureRegistryValue(RegistryHive.LocalMachine, SteamKeyPath, SteamInstallValueName, expectedPath);
		}

		private void EnsureRockstarRegistryValue(string gamePath)
		{
			// Pick the key that matches the edition actually in use, rather than
			// always writing to the legacy one.
			var isEnhanced = IsEnhancedInstallation(gamePath);
			var keyPath = isEnhanced ? RockstarEnhancedKeyPath : RockstarKeyPath;

			EnsureRegistryValue(RegistryHive.LocalMachine, keyPath, RockstarInstallValueName, gamePath);
		}

		/// <summary>
		/// True when the folder holds the Enhanced executable rather than GTA5.exe.
		/// </summary>
		private static bool IsEnhancedInstallation(string gamePath)
		{
			foreach (var name in Constants.GtaExecutableFileNames)
			{
				if (string.Equals(name, Constants.GTA5ExecutableFileName, StringComparison.OrdinalIgnoreCase))
					continue;

				if (File.Exists(Path.Combine(gamePath, name)))
					return true;
			}

			return false;
		}

		private void EnsureRegistryValue(RegistryHive hive, string keyPath, string valueName, string expectedPath)
		{
			using (var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
			using (var key = baseKey.OpenSubKey(keyPath, writable: true))
			{
				if (key == null)
				{
					_logger.Write($"Registry key {keyPath} does not exist, creating it.", LogMessageSeverity.Warning);

					using (var newKey = baseKey.CreateSubKey(keyPath))
					{
						if (newKey == null)
						{
							_logger.Write($"Failed to create registry key {keyPath}.", LogMessageSeverity.Error);
							return;
						}

						newKey.SetValue(valueName, expectedPath, RegistryValueKind.String);
					}

					_logger.Write($"Created {keyPath}\\{valueName} = {expectedPath}");
					return;
				}

				var currentValue = key.GetValue(valueName) as string;

				if (string.Equals(currentValue, expectedPath, StringComparison.OrdinalIgnoreCase))
				{
					_logger.Write($"Registry key {keyPath}\\{valueName} already points at {expectedPath}.");
					return;
				}

				key.SetValue(valueName, expectedPath, RegistryValueKind.String);

				_logger.Write($"Updated {keyPath}\\{valueName} from '{currentValue ?? "(missing)"}' to {expectedPath}.");
			}
		}

		private static bool IsSteamInstallation(string gamePath)
		{
			return File.Exists(Path.Combine(gamePath, Constants.SteamApiDllFileName));
		}
	}
}