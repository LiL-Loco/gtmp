using GTMP.Server.MasterApi.Contracts;

namespace GTMP.Server.MasterApi.Configuration;

/// <summary>
/// What the master API advertises. Bound from <c>masterapi.json</c> next to the
/// executable.
/// </summary>
/// <remarks>
/// Everything is file driven on purpose. The launcher reads this over HTTP
/// precisely so that an operator can change the advertised servers, news and
/// status without rebuilding anything.
/// <para>
/// Field names are the wire names, not the C# property names. Several properties
/// carry an explicit rename, and System.Text.Json matches only the effective name,
/// so a config that spells <c>IpPortAddress</c> instead of <c>ip</c> loads without
/// any error and publishes an empty address. See <see cref="Validate"/>.
/// </para>
/// </remarks>
public sealed class MasterApiSettings
{
    /// <summary>Name of the settings file.</summary>
    public const string FileName = "masterapi.json";

    /// <summary>Game servers listed by <c>servers/detailed</c>.</summary>
    public List<ServerEntry> Servers { get; set; } = new();

    /// <summary>Addresses that count as verified. Subset of <see cref="Servers"/>.</summary>
    public List<string> VerifiedServers { get; set; } = new();

    /// <summary>News shown on the launcher start screen.</summary>
    public List<ForumPost> News { get; set; } = new();

    /// <summary>Status components for <c>api/v1/components</c>.</summary>
    public List<StatusComponent> Components { get; set; } = new();

    /// <summary>Directory holding version files, hashes and update archives.</summary>
    public string ArtifactDirectory { get; set; } = "artifacts";

    /// <summary>
    /// Hash the launcher must present to read a troubleshooting article.
    /// </summary>
    /// <remarks>
    /// The launcher sends this as the <c>hash</c> query parameter. It is a shared
    /// secret for read-only content, so it is compared in constant time and an
    /// empty configured value means the endpoint is disabled rather than open.
    /// </remarks>
    public string ForumApiKey { get; set; } = string.Empty;

    /// <summary>Base address the launcher is told to use for forum articles.</summary>
    public string ForumBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Reports every entry that would be published in a broken state.
    /// </summary>
    /// <remarks>
    /// The alternative is a launcher that quietly shows one server with a blank
    /// address and news entries with no text, and nothing anywhere says why. The
    /// host logs these at startup instead of refusing to start, because a typo in
    /// a data file should not take the master server offline.
    /// </remarks>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        foreach (var server in Servers)
        {
            var reference = string.IsNullOrWhiteSpace(server.ServerName) ? "<unnamed>" : server.ServerName;

            if (string.IsNullOrWhiteSpace(server.IpPortAddress))
            {
                // Almost always a config that used "IpPortAddress" instead of "ip".
                problems.Add($"Servers[{reference}]: \"ip\" is empty. The field is named \"ip\", not \"IpPortAddress\".");
            }

            if (server.Port <= 0)
            {
                problems.Add($"Servers[{reference}]: Port must be greater than zero.");
            }

            if (string.IsNullOrWhiteSpace(server.ServerName))
            {
                problems.Add("Servers: an entry has no ServerName.");
            }
        }

        foreach (var address in VerifiedServers)
        {
            if (Servers.All(server => !string.Equals(server.IpPortAddress, address, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"VerifiedServers: \"{address}\" is not present in Servers.");
            }
        }

        foreach (var post in News)
        {
            if (post.Id == 0)
            {
                problems.Add($"News[\"{post.Title}\"]: \"thread_id\" is missing. The field is named \"thread_id\", not \"Id\".");
            }

            if (string.IsNullOrWhiteSpace(post.Title))
            {
                problems.Add("News: an entry has no Title.");
            }
        }

        foreach (var component in Components)
        {
            if (component.CreatedAt == default)
            {
                problems.Add($"Components[{component.Name}]: \"create_at\" is unset.");
            }
        }

        return problems;
    }
}
