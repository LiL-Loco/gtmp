using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using GrandTheftMultiplayer.Launcher.Helpers;
using GrandTheftMultiplayer.Launcher.Models.Logger;
using GrandTheftMultiplayer.Launcher.Models.Notifications;
using GrandTheftMultiplayer.Launcher.Services.Interfaces;

namespace GrandTheftMultiplayer.Launcher.Services.Runtime
{
	public class CefFilesUpdater : ICefFilesUpdater
	{
		private const string LibCefRelativePath = @"cef\libcef.dll";

		private readonly ILogger _logger;
		private readonly INotificationService _notificationService;
		private readonly ISettingsProvider _settingsProvider;

		public CefFilesUpdater(
			ILogger logger,
			INotificationService notificationService,
			ISettingsProvider settingsProvider)
		{
			_logger = logger;
			_notificationService = notificationService;
			_settingsProvider = settingsProvider;
		}

		public async Task<bool> UpdateCefBrowserFiles()
		{
			_logger.Write("Checking for CEF update.");

			var libCefPath = GetLibCefPath();

			_logger.Write($"CEF lib path: {libCefPath}");

			if (!File.Exists(libCefPath))
			{
				// CEF belongs to the game client, not to the launcher: scripts render
				// their HTML windows through it. The launcher itself embeds no browser -
				// this project uses HtmlRenderer.WPF. A build from source therefore has
				// no libcef.dll, and treating that as a fatal error blocked the launch
				// over a file the launcher never loads.
				_logger.Write(
					$"CEF library \"{libCefPath}\" not found. The game client ships its own CEF runtime, so this does not block the launch.",
					LogMessageSeverity.Warning);

				return true;
			}

			var currentCefVersion = FileVersionInfo.GetVersionInfo(libCefPath)?.ProductVersion;

			string newestCefVersion;
			try
			{
				newestCefVersion = await GetNewestCefVersion();
			}
			catch (Exception ex)
			{
				_logger.Write(ex);
				_notificationService.ShowNotification("Failed to check current CEF version.", true);
				return true;
			}

			_logger.Write($"Current CEF version: {currentCefVersion}, newest CEF version: {newestCefVersion}");

			// An empty answer means the master server has no version for this channel,
			// or that no master server is configured at all. Comparing against it reports
			// a mismatch on every start and asks the user to reinstall a launcher that is
			// perfectly fine. A missing local version string is just as meaningless.
			if (string.IsNullOrWhiteSpace(newestCefVersion) || string.IsNullOrWhiteSpace(currentCefVersion))
			{
				_logger.Write(
					"Cannot compare CEF versions, skipping check.",
					LogMessageSeverity.Warning);

				return true;
			}

			if (currentCefVersion == newestCefVersion)
			{
				_logger.Write("Same version.");
				return true;
			}

			if (!PromptUserForCefUpdate())
			{
				_logger.Write("User chose not to update.");
				return true;
			}

			_notificationService.ShowNotification("CEF update available.", Notification.ShortMessageDelay);

			OpenDownloadPage();
			RequestShutdown();

			return false;
		}

		/// <summary>
		/// Opens the download page the user was sent to.
		/// </summary>
		/// <remarks>
		/// Failures are logged and swallowed: the user already agreed to reinstall, so
		/// a browser that refuses to start must not turn into an unhandled exception.
		/// </remarks>
		private void OpenDownloadPage()
		{
			try
			{
				Process.Start(Constants.DownloadPageUrl.AbsoluteUri);
			}
			catch (Exception ex)
			{
				_logger.Write(ex);
			}
		}

		/// <summary>
		/// Closes the launcher so the user can run the installer.
		/// </summary>
		/// <remarks>
		/// This used to call <c>Process.GetCurrentProcess().Kill()</c>, which terminates
		/// the process without running the WPF shutdown path. Splash screens were never
		/// disposed, and GTA V files patched by an earlier run stayed patched. A graceful
		/// shutdown lets <c>MainWindow.OnClosed</c> run.
		/// </remarks>
		private static void RequestShutdown()
		{
			var application = Application.Current;

			if (application == null)
			{
				Environment.Exit(0);
				return;
			}

			application.Shutdown();
		}

		private static bool PromptUserForCefUpdate()
		{
			return
				MessageBox.Show(
					"A new version of CEF is available!\nTo update to it, you'll have to re-install Grand Theft Multiplayer.\nDo this now?",
					"CEF Update Available", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes;
		}
		
		private static string GetLibCefPath()
		{
			return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LibCefRelativePath);
		}

		private async Task<string> GetNewestCefVersion()
		{
			var versionString = await HttpClientProvider.Client.GetStringAsync(Constants.GetCEFVersionUrl(_settingsProvider.GetCurrentSettings().UpdateChannel));

			return versionString.Trim();
		}
	}
}