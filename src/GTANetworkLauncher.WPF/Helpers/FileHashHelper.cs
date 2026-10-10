using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GrandTheftMultiplayer.Launcher.Helpers
{
	/// <summary>
	/// SHA-256 hashing used to verify downloaded update archives before they are
	/// extracted or executed.
	/// </summary>
	public static class FileHashHelper
	{
		private const int Sha256HexLength = 64;

		/// <summary>
		/// Computes the SHA-256 of a file as a lower case hex string.
		/// </summary>
		public static string ComputeSha256(string filePath)
		{
			if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
				return null;

			using (var stream = File.OpenRead(filePath))
			using (var sha256 = SHA256.Create())
				return ToHex(sha256.ComputeHash(stream));
		}

		/// <summary>
		/// Verifies a file against an expected SHA-256 checksum.
		/// Returns false (and never throws) for a missing file or an unusable expected value.
		/// </summary>
		public static bool MatchesSha256(string filePath, string expectedHash)
		{
			var expectedBytes = ParseExpectedHash(expectedHash);
			if (expectedBytes == null) return false;

			if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;

			byte[] actualBytes;
			try
			{
				using (var stream = File.OpenRead(filePath))
				using (var sha256 = SHA256.Create())
					actualBytes = sha256.ComputeHash(stream);
			}
			catch (IOException)
			{
				return false;
			}
			catch (UnauthorizedAccessException)
			{
				return false;
			}

			return FixedTimeEquals(expectedBytes, actualBytes);
		}

		/// <summary>
		/// Normalizes a checksum as delivered by the masterserver. Tolerates surrounding
		/// whitespace, quotes, a "sha256:" prefix and upper case hex, because all of those
		/// show up in hand edited and generated hash files in practice.
		/// </summary>
		private static byte[] ParseExpectedHash(string expectedHash)
		{
			if (string.IsNullOrWhiteSpace(expectedHash)) return null;

			var normalized = expectedHash.Trim().Trim('"').Trim();

			var prefixIndex = normalized.IndexOf(':');
			if (prefixIndex >= 0) normalized = normalized.Substring(prefixIndex + 1);

			var hex = new StringBuilder(normalized.Length);
			foreach (var c in normalized)
			{
				if (char.IsWhiteSpace(c)) continue;
				if (Uri.IsHexDigit(c)) hex.Append(char.ToLower(c, CultureInfo.InvariantCulture));
			}

			if (hex.Length != Sha256HexLength) return null;

			var bytes = new byte[Sha256HexLength / 2];
			for (var i = 0; i < bytes.Length; i++)
			{
				if (!byte.TryParse(hex.ToString(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
					return null;
			}

			return bytes;
		}

		/// <summary>
		/// Compares in time independent of where the first difference is, so that the
		/// comparison itself cannot be used to guess a checksum byte by byte.
		/// </summary>
		private static bool FixedTimeEquals(byte[] left, byte[] right)
		{
			if (left == null || right == null || left.Length != right.Length) return false;

			var difference = 0;
			for (var i = 0; i < left.Length; i++)
				difference |= left[i] ^ right[i];

			return difference == 0;
		}

		private static string ToHex(byte[] bytes)
		{
			var hex = new StringBuilder(bytes.Length * 2);
			foreach (var b in bytes)
				hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));

			return hex.ToString();
		}
	}
}