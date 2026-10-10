using System.Security.Cryptography;

namespace GTMP.Server.MasterApi.Infrastructure;

/// <summary>Which artifact a request is about.</summary>
public enum ArtifactKind
{
    /// <summary>Supported GTA V version, plain text.</summary>
    Game,

    /// <summary>CEF runtime version, plain text.</summary>
    Cef,

    /// <summary>Launcher version, plain text.</summary>
    Launcher,

    /// <summary>Game client version, plain text.</summary>
    Client
}

/// <summary>
/// Serves versions, archives and their SHA-256 hashes from a directory on disk.
/// </summary>
/// <remarks>
/// The hash is computed from the archive instead of being read from a separate
/// file. The launcher verifies what it downloads against this value, so a
/// hand-maintained hash file could disagree with the artifact and would lock
/// users out of updates.
/// </remarks>
public sealed class ArtifactStore
{
    private readonly string _rootDirectory;
    private readonly Dictionary<string, string> _hashCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Length, DateTime WrittenUtc)> _archiveCache = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Creates a store rooted at <paramref name="rootDirectory"/>.</summary>
    public ArtifactStore(string rootDirectory)
    {
        _rootDirectory = rootDirectory;
    }

    /// <summary>
    /// Returns the version string for a channel, or null when none is published.
    /// </summary>
    public string? GetVersion(ArtifactKind kind, string channel)
    {
        var path = ResolveChannelDirectory(kind, channel);
        if (path is null) return null;

        var versionFile = Path.Combine(path, "version");
        if (!File.Exists(versionFile)) return null;

        try
        {
            var version = File.ReadAllText(versionFile).Trim();
            return string.IsNullOrEmpty(version) ? null : version;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Returns the archive path for a channel, or null when none is published.</summary>
    public string? GetArchivePath(ArtifactKind kind, string channel)
    {
        var path = ResolveChannelDirectory(kind, channel);
        if (path is null) return null;

        var archive = Path.Combine(path, "files.zip");
        return File.Exists(archive) ? archive : null;
    }

    /// <summary>
    /// Returns the lower case hex SHA-256 of the published archive, or null when
    /// no archive exists.
    /// </summary>
    public string? GetArchiveHash(ArtifactKind kind, string channel)
    {
        var archive = GetArchivePath(kind, channel);
        if (archive is null) return null;

        lock (_gate)
        {
            var info = new FileInfo(archive);

            // Recomputed when the file changes, so replacing an archive in place
            // does not keep serving the old digest.
            if (_hashCache.TryGetValue(archive, out var cached) &&
                _archiveCache.TryGetValue(archive, out var cachedInfo) &&
                cachedInfo.Length == info.Length &&
                cachedInfo.WrittenUtc == info.LastWriteTimeUtc)
            {
                return cached;
            }

            using var stream = File.OpenRead(archive);
            var hash = SHA256.HashData(stream);

            var hex = Convert.ToHexString(hash).ToLowerInvariant();

            _hashCache[archive] = hex;
            _archiveCache[archive] = (info.Length, info.LastWriteTimeUtc);

            return hex;
        }
    }

    /// <summary>
    /// Resolves the per-channel directory, refusing anything that is not a plain
    /// channel name.
    /// </summary>
    /// <remarks>
    /// The channel arrives from the URL and ends up in a file path. Without this
    /// check a request for <c>../../appsettings</c> would escape the artifact
    /// root entirely.
    /// </remarks>
    private string? ResolveChannelDirectory(ArtifactKind kind, string channel)
    {
        if (string.IsNullOrWhiteSpace(channel) || !IsSafeChannelName(channel)) return null;

        return Path.Combine(_rootDirectory, "update", channel, kind.ToString().ToLowerInvariant());
    }

    private static bool IsSafeChannelName(string channel)
    {
        if (channel.Length > 64) return false;

        foreach (var character in channel)
        {
            var isAllowed = char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.';

            if (!isAllowed) return false;
        }

        // "." and ".." are made of allowed characters but are directory traversal.
        return channel is not "." and not "..";
    }
}
