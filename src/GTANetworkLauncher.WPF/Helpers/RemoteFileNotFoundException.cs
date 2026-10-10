using System;
using System.Runtime.Serialization;

namespace GrandTheftMultiplayer.Launcher.Helpers
{
	/// <summary>
	/// Thrown when the master server answered, but has nothing published at the
	/// requested URL.
	/// </summary>
	/// <remarks>
	/// This exists because <c>HttpResponseMessage.EnsureSuccessStatusCode</c> throws a
	/// plain <see cref="System.Net.Http.HttpRequestException"/> on .NET Framework, and
	/// that exception carries no <c>StatusCode</c>. Without a distinct type a 404 from
	/// a master that publishes no client archive is indistinguishable from a 500 or a
	/// timeout, so every case ends up as "failed to download" and points the reader at
	/// the network instead of at the missing artifact.
	/// </remarks>
	[Serializable]
	public class RemoteFileNotFoundException : Exception
	{
		/// <summary>URL that answered with 404.</summary>
		public string Url { get; }

		/// <summary>Creates the exception for a URL that is not published.</summary>
		public RemoteFileNotFoundException(string url)
			: base($"Nothing is published at {url}.")
		{
			Url = url;
		}

		/// <summary>Serialization constructor.</summary>
		protected RemoteFileNotFoundException(SerializationInfo info, StreamingContext context)
			: base(info, context)
		{
			Url = info.GetString(nameof(Url));
		}

		/// <inheritdoc />
		public override void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			base.GetObjectData(info, context);

			if (info == null) return;

			info.AddValue(nameof(Url), Url);
		}
	}
}