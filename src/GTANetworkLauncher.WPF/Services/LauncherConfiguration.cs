using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace GrandTheftMultiplayer.Launcher.Services
{
	/// <summary>
	/// Infrastructure the launcher talks to, loaded from <c>launcher.json</c> next to
	/// the executable.
	/// </summary>
	/// <remarks>
	/// The released source hardcoded the masterserver addresses as the placeholder
	/// "https://...", which is not a valid URI. Every network call then failed with an
	/// opaque error while the launcher looked like it was merely offline. Making the
	/// addresses configuration turns that into a state the launcher can name, and gives
	/// whoever deploys it exactly one file to fill in.
	/// <para>
	/// Uses Newtonsoft rather than System.Text.Json: the project targets .NET Framework
	/// and already ships Newtonsoft, so this adds no dependency.
	/// </para>
	/// </remarks>
	public sealed class LauncherConfiguration
	{
		public const string FileName = "launcher.json";

		/// <summary>Base address of the master server. A trailing slash is optional.</summary>
		public string MasterServerAddress { get; set; } = string.Empty;

		/// <summary>Base address of the status server. A trailing slash is optional.</summary>
		public string StatusServerAddress { get; set; } = string.Empty;

		/// <summary>Base address of the forum API used for news and troubleshooting articles.</summary>
		public string ForumsApiUrl { get; set; } = string.Empty;

		/// <summary>
		/// True when every address is a syntactically valid absolute URI.
		/// </summary>
		/// <remarks>
		/// Partial configuration counts as unconfigured on purpose. A launcher that can
		/// reach the server list but not the update channel looks far more broken than
		/// one that says it is not set up.
		/// </remarks>
		[JsonIgnore]
		public bool IsConfigured =>
			IsAbsoluteHttpUrl(MasterServerAddress) &&
			IsAbsoluteHttpUrl(StatusServerAddress) &&
			IsAbsoluteHttpUrl(ForumsApiUrl);

		/// <summary>Names of the settings that still need a value, for the log and the UI.</summary>
		[JsonIgnore]
		public string MissingConfiguration
		{
			get
			{
				var missing = new List<string>();

				if (!IsAbsoluteHttpUrl(MasterServerAddress)) missing.Add(nameof(MasterServerAddress));
				if (!IsAbsoluteHttpUrl(StatusServerAddress)) missing.Add(nameof(StatusServerAddress));
				if (!IsAbsoluteHttpUrl(ForumsApiUrl)) missing.Add(nameof(ForumsApiUrl));

				return string.Join(", ", missing);
			}
		}

		private static bool IsAbsoluteHttpUrl(string value)
		{
			return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
				(string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
				 string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// Reads <c>launcher.json</c>, creating a documented template on first start.
		/// Never throws: a broken file must not stop the launcher from opening.
		/// </summary>
		public static LauncherConfiguration LoadOrCreate(string directory = null)
		{
			var baseDirectory = string.IsNullOrWhiteSpace(directory)
				? AppDomain.CurrentDomain.BaseDirectory
				: directory;

			var path = Path.Combine(baseDirectory, FileName);

			try
			{
				if (File.Exists(path))
				{
					var content = File.ReadAllText(path);

					if (!string.IsNullOrWhiteSpace(content))
					{
						return JsonConvert.DeserializeObject<LauncherConfiguration>(content) ?? new LauncherConfiguration();
					}
				}

				var template = new LauncherConfiguration();
				File.WriteAllText(path, JsonConvert.SerializeObject(template, Formatting.Indented));
				return template;
			}
			catch
			{
				// A malformed or unwritable configuration leaves the launcher in its
				// unconfigured state, which it reports clearly, rather than crashing.
				return new LauncherConfiguration();
			}
		}
	}
}