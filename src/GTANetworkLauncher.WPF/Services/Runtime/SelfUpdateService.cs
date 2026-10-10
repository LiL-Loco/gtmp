using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using GrandTheftMultiplayer.Launcher.Helpers;
using GrandTheftMultiplayer.Launcher.Models.Logger;
using GrandTheftMultiplayer.Launcher.Services.Interfaces;

namespace GrandTheftMultiplayer.Launcher.Services.Runtime
{
	public class SelfUpdateService : ISelfUpdateService
	{
	    private const string UpdateFilePath = "tempstorage";
	    private const string UpdateFileName = "launcher_files.zip";

        private readonly ILogger _logger;
		private readonly INotificationService _notificationService;
		private readonly ISettingsProvider _settingsProvider;

		public event EventDelegates.UpdateStatusChangedHandler UpdateStatusChanged;

		public SelfUpdateService(
			ILogger logger,
			INotificationService notificationService,
			ISettingsProvider settingsProvider)
		{
			_logger = logger;
			_notificationService = notificationService;
			_settingsProvider = settingsProvider;
		}

		public async Task<bool> IsUpdateAvailable()
		{
			if (!Constants.IsConfigured)
			{
				WriteNotConfigured();
				_notificationService.ShowNotification(Constants.NotConfiguredMessage, true);
				return false;
			}

			_logger.Write("Checking for launcher update.");

			var currentVersion = GetCurrentVersion();
			var newestVersion = await TryGetLatestVersion();

			_logger.Write($"Current version: {currentVersion}, newest version: {newestVersion}");

			// A failing version check must not be treated as "up to date" and must not
			// crash: Version's comparison operator dereferences the left operand.
			if (newestVersion == null || currentVersion == null) return false;

			return newestVersion > currentVersion;
		}

		/// <summary>
		/// Writes the reason the launcher cannot reach any server, once, in a form an
		/// operator can act on.
		/// </summary>
		private void WriteNotConfigured(LogMessageSeverity severity = LogMessageSeverity.Warning)
		{
			_logger.Write(Constants.NotConfiguredMessage, severity);
		}
		private async Task<Version> TryGetLatestVersion()
		{
			try
			{
				return await GetLatestLauncherVersion();
			}
			catch (Exception e)
			{
				_logger.Write(e);
				_notificationService.ShowNotification("Failed to check for launcher update.", true);
			}

			return null;
		}

		private async Task<Version> GetLatestLauncherVersion()
		{
			var url = Constants.GetLauncherVersionUrl(_settingsProvider.GetCurrentSettings().UpdateChannel);
			var versionString = await HttpClientProvider.Client.GetStringAsync(url);

			return Version.Parse(versionString.Trim());
		}

		private static Version GetCurrentVersion()
		{
		    var executingFileVersion = VersionHelper.VersionFromFile(Assembly.GetExecutingAssembly().Location);

            return executingFileVersion;
		}

		public async Task UpdateSelf()
		{
			var applicationFileName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
		    FileOperationsHelper.EnsureFileDirectory(GetUpdateDownloadPath());

			var updateChannel = _settingsProvider.GetCurrentSettings().UpdateChannel;

			if (!await TryDownloadNewLauncher(GetUpdateDownloadPath(), updateChannel))
			{
				_notificationService.ShowNotification("Failed to download new launcher.", true);
				return;
			}

			if (!await VerifyUpdateIntegrity(GetUpdateDownloadPath(), updateChannel))
			{
				TryDeleteQuarantineFile(GetUpdateDownloadPath());
				_notificationService.ShowNotification("The downloaded update could not be verified and was discarded. Please try again later.", true);
				return;
			}

		    if (!await ExtractUpdate(GetUpdateDownloadPath()))
		    {
		        _notificationService.ShowNotification("Failed to extract launcher update.", true);
                return;
		    }

            var updateBatTargetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update.bat");
			if (!await TryExtractUpdateBat(updateBatTargetPath))
			{
				_notificationService.ShowNotification("Failed to perform update.", true);
				return;
			}

			Process.Start(updateBatTargetPath, applicationFileName);
			Process.GetCurrentProcess().Kill();
		}

	    private async Task<bool> ExtractUpdate(string updateFilePath)
	    {
	        var extractionPath = GetUpdateDownloadFolder();
	        _logger.Write($"Extracting launcher files {updateFilePath} to {extractionPath}");

	        try
	        {
	            await ZipExtractor.ExtractToDirectoryAsync(updateFilePath, extractionPath, true);

                return true;
	        }
	        catch (Exception ex)
	        {
	            _logger.Write(ex);
	        }

	        return false;
	    }

        private async Task<bool> TryExtractUpdateBat(string updateBatTargetPath)
		{
			_logger.Write("Extracting update.bat");

			try
			{
				var resourceStream = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/Update/update.bat"));
			    if (resourceStream == null)
			    {
			        _logger.Write("Could not read update.bat from application resources.");
			        return false;
			    }

				var updateBatBytes = new byte[resourceStream.Stream.Length];
				await resourceStream.Stream.ReadAsync(updateBatBytes, 0, updateBatBytes.Length);

				File.WriteAllBytes(updateBatTargetPath, updateBatBytes);

				return true;
			}
			catch (Exception e)
			{
				_logger.Write(e);
			}

			return false;
		}

		private async Task<bool> TryDownloadNewLauncher(string downloadTargetPath, string updateChannel)
		{
			var downloadLauncherUrl = Constants.GetLauncherFileDownloadUrl(updateChannel);
			_logger.Write("Downloading new launcher.");

			try
			{
				HttpClientProvider.EnsureSecureUrl(downloadLauncherUrl);

				using (var httpClientWithProgress = new HttpClientDownloadWithProgress(downloadLauncherUrl, downloadTargetPath))
				{
					httpClientWithProgress.ProgressChanged += (size, downloaded, percentage) => UpdateStatusChanged?.Invoke(percentage ?? 50);
					await httpClientWithProgress.StartDownload();
				}

				return true;
			}
			catch (RemoteFileNotFoundException ex)
			{
				_logger.Write(
					$"Master server publishes no launcher archive for channel \"{updateChannel}\" ({ex.Url}).",
					LogMessageSeverity.Error);
			}
			catch (Exception e)
			{
				_logger.Write(e);
			}

			return false;
		}

		/// <summary>
		/// Compares the SHA-256 of the downloaded archive against the checksum published
		/// by the masterserver. Nothing is extracted before this check passes.
		/// </summary>
		private async Task<bool> VerifyUpdateIntegrity(string updateFilePath, string updateChannel)
		{
			_logger.Write("Verifying checksum of the downloaded launcher.");

			try
			{
				var hashUrl = Constants.GetLauncherFileHashUrl(updateChannel);
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
			catch (Exception e)
			{
				_logger.Write(e);
			}

			return false;
		}

		private void TryDeleteQuarantineFile(string filePath)
		{
			try
			{
				if (File.Exists(filePath)) File.Delete(filePath);
			}
			catch (Exception e)
			{
				_logger.Write(e);
			}
		}

	    private static string GetUpdateDownloadFolder()
	    {
	        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UpdateFilePath);
	    }

        private static string GetUpdateDownloadPath()
	    {
	        return Path.Combine(GetUpdateDownloadFolder(), UpdateFileName);
	    }
    }
}