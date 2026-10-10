using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using GrandTheftMultiplayer.Launcher.Helpers;
using GrandTheftMultiplayer.Launcher.Models.Logger;
using GrandTheftMultiplayer.Launcher.Models.Notifications;
using GrandTheftMultiplayer.Launcher.Models.SplashScreenProgressProvider;
using GrandTheftMultiplayer.Launcher.Services.Interfaces;

namespace GrandTheftMultiplayer.Launcher.Services.Runtime
{
	public class ClientFilesUpdater : IClientFilesUpdater
	{
		private const string GtMultiplayerBinFolder = "bin";
		private const string GrandTheftMultiplayerDllRelativePath = GtMultiplayerBinFolder + @"\scripts\GrandTheftMultiplayer.Client.dll";
	    private const string UpdateFolder = @"tempstorage\";
		private const string UpdateFilePath = UpdateFolder + @"files.zip";

		private readonly ILogger _logger;
		private readonly INotificationService _notificationService;
		private readonly ISplashScreenProgressProvider _splashScreenProgressProvider;
		private readonly ISettingsProvider _settingsProvider;

		public ClientFilesUpdater(
			ILogger logger,
			INotificationService notificationService,
			ISplashScreenProgressProvider splashScreenProgressProvider,
			ISettingsProvider settingsProvider)
		{
			_logger = logger;
			_notificationService = notificationService;
			_splashScreenProgressProvider = splashScreenProgressProvider;
			_settingsProvider = settingsProvider;
		}

		public async Task<bool> UpdateClientFiles()
		{
			// Without a master server there is nowhere to fetch the client from. Reporting
			// that as a failed download pointed operators at the network instead of at
			// the configuration file, and the underlying UriFormatException named an
			// empty string, which says nothing at all.
			if (!Constants.IsConfigured)
			{
				_logger.Write(Constants.NotConfiguredMessage, LogMessageSeverity.Warning);
				_notificationService.ShowNotification(Constants.NotConfiguredMessage, true);

				// The client assembly is what the game loads. Without it the launch cannot
				// continue, so this stays fatal - unlike the CEF check, which guards a file
				// the launcher never reads.
				return false;
			}

			var currentClientVersion = GetCurrentClientVersion();
			if (currentClientVersion == null)
				return await TryDownloadNewClientFiles();

			var newestClientVersion = await TryGetNewestClientVersion();
			if (newestClientVersion == null)
				return true;

			_logger.Write($"Current client version: {currentClientVersion}, newest client version: {newestClientVersion}");

			if (currentClientVersion == newestClientVersion)
				return true;

#if DEBUG
		    return true;
#endif

            if (!ShouldDownloadNewClientVersion(currentClientVersion, newestClientVersion))
				return true;

			_splashScreenProgressProvider.SetCurrentStep(SplashScreenProgressStep.DownloadNewClient);
			return await TryDownloadNewClientFiles();
		}

		private bool ShouldDownloadNewClientVersion(Version currentVersion, Version newVersion)
		{
			var downloadNewestVersion =
				MessageBox.Show(
					$"New version of GT-MP available.\nCurrent version: {currentVersion}\nNew version: {newVersion}\n\nDownload now?",
					"New Version Available",
					MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes;

			if (!downloadNewestVersion)
				_logger.Write("User chose not to download newest client version.");

			return downloadNewestVersion;
		}

		private async Task<bool> TryDownloadNewClientFiles()
		{
			_logger.Write("Downloading new client files.");
			try
			{
				var success = await DownloadNewClientFiles();
				return success;
			}
			catch (RemoteFileNotFoundException ex)
			{
				// The master answered and has no client for this channel. That is a
				// deployment gap, not a network failure, and saying "failed to download"
				// sent people looking at their connection instead of at the master.
				var channel = _settingsProvider.GetCurrentSettings().UpdateChannel;

				_logger.Write(
					$"Master server publishes no client archive for channel \"{channel}\" ({ex.Url}). The game cannot start without {GrandTheftMultiplayerDllRelativePath}.",
					LogMessageSeverity.Error);

				_notificationService.ShowNotification(
					$"The master server does not provide a client for update channel \"{channel}\".",
					true);

				return false;
			}
			catch (Exception ex)
			{
				_logger.Write(ex);
				_notificationService.ShowNotification("Failed to download client files.", true);
				return false;
			}
		}

		private async Task<bool> DownloadNewClientFiles()
		{
			var updateFilePath = GetUpdateDownloadPath();
			FileOperationsHelper.EnsureFileDirectory(updateFilePath);

			var updateChannel = _settingsProvider.GetCurrentSettings().UpdateChannel;
			var downloadUpdateUrl = Constants.GetUpdateFileDownloadUrl(updateChannel);

			HttpClientProvider.EnsureSecureUrl(downloadUpdateUrl);

			using (var client = new HttpClientDownloadWithProgress(downloadUpdateUrl, updateFilePath))
			{
				//client.ProgressChanged += (size, downloaded, percentage) =>
				//	_splashScreenProgressProvider.UpdateStep(LaunchStep.DownloadNewClient, LaunchStepStatus.Processing, percentage);

				await client.StartDownload();
			}

			if (!File.Exists(updateFilePath))
			{
				_notificationService.ShowNotification("Failed to download update.", true);
				return false;
			}

			if (!await VerifyUpdateIntegrity(updateFilePath, updateChannel))
			{
				TryDeleteQuarantineFile(updateFilePath);
				_notificationService.ShowNotification("The downloaded update could not be verified and was discarded. Please try again later.", true);
				return false;
			}

			return await ExtractUpdate(updateFilePath);
		}

		/// <summary>
		/// Compares the SHA-256 of the downloaded archive against the checksum published
		/// by the masterserver. The archive is not extracted before this check passes.
		/// </summary>
		private async Task<bool> VerifyUpdateIntegrity(string updateFilePath, string updateChannel)
		{
			_logger.Write("Verifying checksum of the downloaded client files.");

			try
			{
				var hashUrl = Constants.GetUpdateFileHashUrl(updateChannel);
				HttpClientProvider.EnsureSecureUrl(hashUrl);

				var expectedHash = (await HttpClientProvider.Client.GetStringAsync(hashUrl)).Trim();

				var matches = FileHashHelper.MatchesSha256(updateFilePath, expectedHash);

				if (!matches)
				{
					var actualHash = FileHashHelper.ComputeSha256(updateFilePath);
					_logger.Write($"Checksum mismatch. Expected {expectedHash}, got {actualHash}.", LogMessageSeverity.Error);
				}

				return matches;
			}
			catch (Exception ex)
			{
				_logger.Write(ex);
			}

			return false;
		}

		private void TryDeleteQuarantineFile(string filePath)
		{
			try
			{
				if (File.Exists(filePath)) File.Delete(filePath);
			}
			catch (Exception ex)
			{
				_logger.Write(ex);
			}
		}

		private async Task<bool> ExtractUpdate(string updateFilePath)
		{
			var extractionPath = GetUpdateExtractionPath();
			_logger.Write($"Extracting client files {updateFilePath} to {extractionPath}");

			try
			{
			    CleanupClientDirectory(extractionPath);
                await ZipExtractor.ExtractToDirectoryAsync(updateFilePath, extractionPath, true);
			    Directory.Delete(GetUpdateDownloadFolder(), true);

                return true;
			}
			catch (Exception ex)
			{
				_logger.Write(ex);
				_notificationService.ShowNotification("Failed to extract update.", true);
			}

			return false;
		}

		private Version GetCurrentClientVersion()
		{
			var grandTheftMultiplayerDllPath = GetGrandTheftMultiplayerkDllPath();
			_logger.Write($"Checking current client version of file {grandTheftMultiplayerDllPath}");

			if (!File.Exists(grandTheftMultiplayerDllPath))
			{
				_logger.Write("File is missing.", LogMessageSeverity.Error);
				_notificationService.ShowNotification($"Missing \"{grandTheftMultiplayerDllPath}\".", Notification.ShortMessageDelay);
				return null;
			}
			
			var version = VersionHelper.VersionFromFile(grandTheftMultiplayerDllPath);
			_logger.Write($"Current client version: {version}");
			return version;
		}

		private async Task<Version> TryGetNewestClientVersion()
		{
			try
			{
				_logger.Write("Getting newest client files version.");
				return await GetNewestClientVersion();
			}
			catch (Exception ex)
			{
				_logger.Write(ex);
				_notificationService.ShowNotification("Failed to check for newest client version.", true);
			}

			return null;
		}
		
		private async Task<Version> GetNewestClientVersion()
		{
			var versionString = await HttpClientProvider.Client.GetStringAsync(Constants.GetClientVersionUrl(_settingsProvider.GetCurrentSettings().UpdateChannel));

			return Version.Parse(versionString.Trim());
		}

	    private void CleanupClientDirectory(string path)
	    {
	        foreach (var file in Directory.GetFiles(path, "*.pdb", SearchOption.AllDirectories))
	        {
	            File.Delete(file);
	        }
        }

		private static string GetGrandTheftMultiplayerkDllPath()
		{
			return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, GrandTheftMultiplayerDllRelativePath);
		}

	    private static string GetUpdateDownloadFolder()
	    {
	        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UpdateFolder);
	    }

        private static string GetUpdateDownloadPath()
		{
			return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UpdateFilePath);
		}

		private static string GetUpdateExtractionPath()
		{
			return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, GtMultiplayerBinFolder);
		}
	}
}