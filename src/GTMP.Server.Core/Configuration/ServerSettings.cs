namespace GTMP.Server.Core.Configuration;

/// <summary>
/// Server configuration, loaded from server.json next to the executable.
/// </summary>
public sealed class ServerSettings
{
    public string ServerName { get; set; } = "GTMP Development Server";

    /// <summary>UDP port the game protocol listens on.</summary>
    public int Port { get; set; } = 4499;

    public int MaxPlayers { get; set; } = 32;

    /// <summary>Password required to join. Empty means no password.</summary>
    public string Password { get; set; } = string.Empty;

    public string Gametype { get; set; } = "freeroam";

    public string Website { get; set; } = string.Empty;

    /// <summary>Directory containing resources, relative to the executable.</summary>
    public string ResourceFolder { get; set; } = "resources";

    /// <summary>
    /// Length of one simulation tick in milliseconds. Position data is replicated on
    /// this cadence, so it trades bandwidth against responsiveness.
    /// </summary>
    public int TickRate { get; set; } = 50;

    /// <summary>
    /// Rejects clients whose reported version does not match this one.
    /// </summary>
    public string ProtocolVersion { get; set; } = "0.1";

    public bool AnnounceToMasterServer { get; set; }

    public string MasterServerUrl { get; set; } = string.Empty;

    public void Validate()
    {
        if (Port is < 1 or > 65535)
            throw new InvalidOperationException($"Port {Port} is outside the valid range 1-65535.");

        if (TickRate is < 10 or > 1000)
            throw new InvalidOperationException($"Tick rate {TickRate} must be between 10 and 1000 ms.");

        if (MaxPlayers < 1)
            throw new InvalidOperationException("MaxPlayers must be at least 1.");

        if (AnnounceToMasterServer && string.IsNullOrWhiteSpace(MasterServerUrl))
            throw new InvalidOperationException("MasterServerUrl must be set when AnnounceToMasterServer is enabled.");
    }

    public int TicksPerSecond => TickRate > 0 ? 1000 / TickRate : 20;
}