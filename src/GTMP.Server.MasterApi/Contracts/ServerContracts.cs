using System.Text.Json.Serialization;

namespace GTMP.Server.MasterApi.Contracts;

/// <summary>One advertised game server, shaped exactly like the launcher's <c>ServerInfo</c>.</summary>
/// <remarks>
/// The launcher deserializes with Newtonsoft and default settings, so property names
/// are matched case-insensitively but written in the C# casing. Only <c>ip</c> carries
/// an explicit name. Emitting camelCase here would leave the server list silently
/// empty, so the shape is pinned by tests.
/// <para>
/// <see cref="IpPortAddress"/> is not an address. It is "host:port" and the launcher
/// splits it itself.
/// </para>
/// </remarks>
public sealed class ServerEntry
{
    /// <summary>Port, repeated separately because the launcher reads it as its own field.</summary>
    public int Port { get; set; }

    /// <summary>Player slots.</summary>
    public short MaxPlayers { get; set; }

    /// <summary>Display name.</summary>
    public string ServerName { get; set; } = string.Empty;

    /// <summary>Players currently connected.</summary>
    public short CurrentPlayers { get; set; }

    /// <summary>Active gamemode.</summary>
    public string GameMode { get; set; } = string.Empty;

    /// <summary>"host:port". The key the launcher deduplicates on.</summary>
    [JsonPropertyName("ip")]
    public string IpPortAddress { get; set; } = string.Empty;

    /// <summary>Whether joining requires a password.</summary>
    public bool Passworded { get; set; }

    /// <summary>Hostname, when the server announces one.</summary>
    public string Fqdn { get; set; } = string.Empty;

    /// <summary>Server build version.</summary>
    public string ServerVersion { get; set; } = string.Empty;
}

/// <summary>Envelope for <c>servers/detailed</c>.</summary>
public sealed class ServerListResponse
{
    /// <summary>Advertised servers.</summary>
    public List<ServerEntry> List { get; set; } = new();
}

/// <summary>Envelope for <c>servers/verified</c>.</summary>
/// <remarks>
/// Deliberately asymmetric to <see cref="ServerListResponse"/>: this one is a plain
/// string array of "host:port" values, not objects.
/// </remarks>
public sealed class VerifiedServersResponse
{
    /// <summary>Addresses that count as verified.</summary>
    public string[] List { get; set; } = Array.Empty<string>();
}

/// <summary>Body of <c>stats/masterlist</c>.</summary>
public sealed class StatisticsResponse
{
    /// <summary>Players across every advertised server.</summary>
    public int TotalPlayersOnline { get; set; }

    /// <summary>Advertised servers that are on the verified list.</summary>
    public int TotalVerifiedServersOnline { get; set; }

    /// <summary>Players across verified servers only.</summary>
    public int TotalPlayersListedOnline { get; set; }

    /// <summary>Servers exposed in the launcher list.</summary>
    public int TotalListedServersOnline { get; set; }

    /// <summary>Servers known to the master.</summary>
    public int TotalServersOnline { get; set; }
}
