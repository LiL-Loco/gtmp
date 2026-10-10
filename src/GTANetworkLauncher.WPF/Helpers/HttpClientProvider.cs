using System;
using System.Net.Http;

namespace GrandTheftMultiplayer.Launcher.Helpers
{
	/// <summary>
	/// Owns the single HttpClient instance used for API and update requests.
	/// </summary>
	/// <remarks>
	/// Every HttpClient owns its own connection handler and pool. Creating one per
	/// request - as the original code did in every service - exhausts sockets once the
	/// server browser or the status poller keeps polling.
	/// </remarks>
	public static class HttpClientProvider
	{
		private static readonly Lazy<HttpClient> SharedClient = new Lazy<HttpClient>(() => new HttpClient
		{
			Timeout = TimeSpan.FromMinutes(5)
		});

		public static HttpClient Client => SharedClient.Value;

		/// <summary>
		/// Guards against a downgrade to plain HTTP. Update payloads are extracted and
		/// executed by an application that runs elevated, so transport level integrity
		/// is a requirement, not a preference.
		/// </summary>
		/// <remarks>
		/// Loopback is exempt. A self-hosted master server on the same machine speaks
		/// plain HTTP, and forcing TLS there would mean running a certificate the
		/// machine does not trust. The exemption is deliberately narrow: only the
		/// literal loopback range, never a hostname that merely resolves to it.
		/// </remarks>
		public static void EnsureSecureUrl(string url)
		{
			if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
				throw new UriFormatException($"Update URL is not a valid absolute URI: {url}");

			if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
				return;

			if (uri.IsLoopback && string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
				return;

			throw new InvalidOperationException($"Refusing to use non-HTTPS URL for updates: {url}");
		}
	}
}