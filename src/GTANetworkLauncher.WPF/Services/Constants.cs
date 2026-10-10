using System;

namespace GrandTheftMultiplayer.Launcher.Services
{
	/// <summary>
	/// Well known file names and the URLs derived from <see cref="LauncherConfiguration"/>.
	/// </summary>
	/// <remarks>
	/// Infrastructure addresses are read from <c>launcher.json</c> next to the
	/// executable rather than baked into the binary. See
	/// <see cref="LauncherConfiguration"/> for why the released placeholders were a
	/// problem.
	/// </remarks>
	public static class Constants
	{
		private const string DefaultUpdateChannel = "stable";

		private static readonly LauncherConfiguration Configuration = LauncherConfiguration.LoadOrCreate();

		public const int DefaultGamePort = 4499;

		// Generic
		public const string GTA5ExecutableFileName = "GTA5.exe";

		/// <summary>
		/// Executable names GTA V ships under, in the order they are probed.
		/// </summary>
		/// <remarks>
		/// Rockstar ships two editions with different executable names. The legacy
		/// build is GTA5.exe; the Enhanced edition is GTA5_Enhanced.exe and lives in a
		/// "Grand Theft Auto V Enhanced" folder. The launcher previously hardcoded
		/// GTA5.exe in five places, so an Enhanced-only installation was reported as
		/// "Selected path is invalid" no matter what the user picked.
		/// <para>
		/// GTA5_Enhanced_BE.exe is deliberately absent: that is the Rockstar
		/// bootstrapper, not the game.
		/// </para>
		/// </remarks>
		public static readonly string[] GtaExecutableFileNames =
		{
			GTA5ExecutableFileName,
			"GTA5_Enhanced.exe"
		};

		/// <summary>Folder names GTA V is installed into, legacy first.</summary>
		public static readonly string[] GtaInstallFolderNames =
		{
			"Grand Theft Auto V",
			"Grand Theft Auto V Enhanced"
		};

		public const string SteamApiDllFileName = "steam_api64.dll";
		public const string ServerExeFileName = "GrandTheftMultiplayer.Server.exe";
		public const string ForumsApiKey = "LAUNCHER_NEWS_API_KEY";
		public const string SettingsFileName = "settings.xml";

		/// <summary>
		/// Target of the EULA link in the startup prompt.
		/// </summary>
		/// <remarks>
		/// This must be a syntactically valid absolute URI. The released source shipped
		/// "https://..." here, which is not a valid URI, and WPF parses NavigateUri
		/// through UriTypeConverter while loading the window - so the launcher crashed
		/// with an unhandled XamlParseException before the main window ever appeared.
		/// A placeholder that does not parse is worse than no placeholder at all.
		/// Replace with the real EULA before shipping.
		/// <para>
		/// Typed as Uri, not string: XAML's x:Static hands the value straight to the
		/// dependency property without running the type converter, so a string would
		/// land on Hyperlink.NavigateUri and throw ArgumentException at load time.
		/// </para>
		/// </remarks>
		public static readonly Uri EulaUrl = new Uri("https://example.com/eula");

		/// <summary>
		/// Page opened when the launcher asks the user to reinstall for a CEF update.
		/// </summary>
		/// <remarks>
		/// Replaces the literal "https://..." the released source passed to
		/// <c>Process.Start</c>, which throws on a malformed URI. Typed as Uri for the
		/// same reason as <see cref="EulaUrl"/>. Placeholder - replace before shipping.
		/// </remarks>
		public static readonly Uri DownloadPageUrl = new Uri("https://example.com/download");

		// Infrastructure, from launcher.json
		public static string MasterServerAddress => Configuration.MasterServerAddress;

		public static string StatusServerAddress => Configuration.StatusServerAddress;

		public static string ForumsApiUrl => Configuration.ForumsApiUrl;

		/// <summary>True when every infrastructure address is usable.</summary>
		public static bool IsConfigured => Configuration.IsConfigured;

		/// <summary>Names of the settings that still need a value, for diagnostics.</summary>
		public static string MissingConfiguration => Configuration.MissingConfiguration;

		/// <summary>
		/// One sentence naming what is missing and where to set it.
		/// </summary>
		/// <remarks>
		/// Every service that needs the network has to report the same reason. Without a
		/// shared wording each of them invented its own, and an unconfigured launcher
		/// showed up as a download failure or a transport error.
		/// </remarks>
		public static string NotConfiguredMessage =>
			$"Launcher is not configured. Missing in {LauncherConfiguration.FileName}: {MissingConfiguration}.";

		// API URLs
		public static string ServerApiUrl => Combine(MasterServerAddress, "servers/detailed");

		public static string StatisticsUrl => Combine(MasterServerAddress, "stats/masterlist");

		public static string VerfiedServersUrl => Combine(MasterServerAddress, "servers/verified");

		public static string ServerStatusUrl => Combine(StatusServerAddress, "api/v1/components");

		// Formatted API URLs
		private const string SupportedGta5Version = "update/{0}/game/version";
		private const string CEFVersion = "update/{0}/cef/version";
		private const string LauncherVersion = "update/{0}/launcher/version";
		private const string LauncherFileDownload = "update/{0}/launcher/files";
		private const string ClientVersion = "update/{0}/client/version";
		private const string UpdateFileDownload = "update/{0}/client/files";

		// SHA-256 checksums of the archives served by the endpoints above.
		// Plain hex, case insensitive, no prefixes. Returned by the masterserver next
		// to the download link and verified before anything is extracted or executed.
		private const string LauncherFileHash = "update/{0}/launcher/hash";
		private const string UpdateFileHash = "update/{0}/client/hash";

		public static string GetSupportedGta5VersionUrl(string updateChannel = DefaultUpdateChannel) =>
			Combine(MasterServerAddress, SupportedGta5Version, updateChannel);

		public static string GetCEFVersionUrl(string updateChannel = DefaultUpdateChannel) =>
			Combine(MasterServerAddress, CEFVersion, updateChannel);

		public static string GetLauncherVersionUrl(string updateChannel = DefaultUpdateChannel) =>
			Combine(MasterServerAddress, LauncherVersion, updateChannel);

		public static string GetLauncherFileDownloadUrl(string updateChannel = DefaultUpdateChannel) =>
			Combine(MasterServerAddress, LauncherFileDownload, updateChannel);

		public static string GetClientVersionUrl(string updateChannel = DefaultUpdateChannel) =>
			Combine(MasterServerAddress, ClientVersion, updateChannel);

		public static string GetUpdateFileDownloadUrl(string updateChannel = DefaultUpdateChannel) =>
			Combine(MasterServerAddress, UpdateFileDownload, updateChannel);

		public static string GetLauncherFileHashUrl(string updateChannel = DefaultUpdateChannel) =>
			Combine(MasterServerAddress, LauncherFileHash, updateChannel);

		public static string GetUpdateFileHashUrl(string updateChannel = DefaultUpdateChannel) =>
			Combine(MasterServerAddress, UpdateFileHash, updateChannel);

		/// <summary>
		/// Builds an absolute endpoint URL, or an empty string when the launcher has no
		/// master server configured. Callers check <see cref="IsConfigured"/> before
		/// using the result, so they can report the real reason instead of a bogus URI
		/// failure.
		/// </summary>
		private static string Combine(string baseAddress, string relativePath, string updateChannel)
		{
			if (string.IsNullOrWhiteSpace(baseAddress)) return string.Empty;

			return baseAddress.TrimEnd('/') + "/" + string.Format(relativePath, updateChannel);
		}

		/// <summary>
		/// Same contract for paths without a channel placeholder.
		/// </summary>
		/// <remarks>
		/// These used to be plain concatenations. With no master server configured that
		/// produced a relative URI like "servers/detailed", which HttpClient rejects
		/// with an InvalidRequestUriException that says nothing about the real cause.
		/// </remarks>
		private static string Combine(string baseAddress, string relativePath)
		{
			if (string.IsNullOrWhiteSpace(baseAddress)) return string.Empty;

			return baseAddress.TrimEnd('/') + "/" + relativePath;
		}
	}
}