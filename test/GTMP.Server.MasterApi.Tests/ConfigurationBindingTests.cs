using System.Text.Json;

namespace GTMP.Server.MasterApi.Tests;

/// <summary>
/// Covers how <c>masterapi.json</c> binds to <c>MasterApiSettings</c>.
/// </summary>
/// <remarks>
/// This is where a configuration mistake hides: System.Text.Json matches a
/// property only by its effective name, and <c>IpPortAddress</c> carries an
/// explicit <c>[JsonPropertyName("ip")]</c>. A config that spells the C#
/// property name instead loads without an error and silently publishes an empty
/// address, which the launcher then shows as a blank server entry.
/// </remarks>
public sealed class ConfigurationBindingTests
{
    private const string BindOptions =
        """
        {
          "Servers": [
            {
              "Port": 4499,
              "MaxPlayers": 100,
              "ServerName": "GT-MP Development",
              "CurrentPlayers": 3,
              "GameMode": "Freeroam",
              "ip": "127.0.0.1:4499",
              "Passworded": true,
              "Fqdn": "127.0.0.1",
              "ServerVersion": "1.0.0"
            }
          ],
          "VerifiedServers": [ "127.0.0.1:4499" ],
          "ArtifactDirectory": "artifacts",
          "ForumApiKey": "secret"
        }
        """;

    private static Configuration.MasterApiSettings Bind(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        return JsonSerializer.Deserialize<Configuration.MasterApiSettings>(json, options)!;
    }

    [Fact]
    public void IpFieldBindsFromItsWireName()
    {
        var settings = Bind(BindOptions);

        var server = Assert.Single(settings.Servers);
        Assert.Equal("127.0.0.1:4499", server.IpPortAddress);
    }

    [Fact]
    public void EveryOtherFieldBinds()
    {
        var settings = Bind(BindOptions);

        var server = Assert.Single(settings.Servers);
        Assert.Equal(4499, server.Port);
        Assert.Equal(100, server.MaxPlayers);
        Assert.Equal("GT-MP Development", server.ServerName);
        Assert.Equal(3, server.CurrentPlayers);
        Assert.Equal("Freeroam", server.GameMode);
        Assert.True(server.Passworded);
        Assert.Equal("1.0.0", server.ServerVersion);
        Assert.Equal("secret", settings.ForumApiKey);
        Assert.Equal(new[] { "127.0.0.1:4499" }, settings.VerifiedServers);
    }

    [Fact]
    public void ClrPropertyNameDoesNotBindWhenARenameIsInEffect()
    {
        // Pinned deliberately: this is the mistake an operator makes, and the
        // failure mode is a silently empty address rather than an error.
        const string wrongKey =
            """
            { "Servers": [ { "IpPortAddress": "127.0.0.1:4499" } ] }
            """;

        var settings = Bind(wrongKey);

        Assert.Equal(string.Empty, Assert.Single(settings.Servers).IpPortAddress);
    }

    [Fact]
    public void EmptyFileYieldsUsableDefaults()
    {
        var settings = Bind("{}");

        Assert.Empty(settings.Servers);
        Assert.Empty(settings.VerifiedServers);
        Assert.Empty(settings.News);
        Assert.Equal("artifacts", settings.ArtifactDirectory);
        Assert.Equal(string.Empty, settings.ForumApiKey);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreTolerated()
    {
        const string annotated =
            """
            {
              // the only server we run
              "ForumApiKey": "secret",
            }
            """;

        var settings = Bind(annotated);

        Assert.Equal("secret", settings.ForumApiKey);
    }
}
