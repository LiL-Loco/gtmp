using System.Text.Json;
using GTMP.Server.MasterApi.Configuration;
using GTMP.Server.MasterApi.Contracts;

namespace GTMP.Server.MasterApi.Tests;

/// <summary>
/// Pins the exact JSON the launcher receives.
/// </summary>
/// <remarks>
/// Every assertion here exists because of a way the launcher can silently fail:
/// it deserializes with Newtonsoft using default settings, so a renamed or
/// camelCased property does not throw, it yields an empty server list and a
/// blank status page. Asserting on property names, not on deserialized values,
/// is what makes the regression visible.
/// </remarks>
public sealed class ContractShapeTests
{
    private static MasterApiSettings BuildSettings() => new()
    {
        Servers =
        {
            new ServerEntry
            {
                Port = 22005,
                MaxPlayers = 100,
                ServerName = "Test Freeroam",
                CurrentPlayers = 7,
                GameMode = "Freeroam",
                IpPortAddress = "127.0.0.1:22005",
                Passworded = false,
                Fqdn = "play.example.com",
                ServerVersion = "1.0.0"
            }
        },
        VerifiedServers = { "127.0.0.1:22005" },
        Components =
        {
            new StatusComponent
            {
                Id = 1,
                Name = "Master Server",
                Description = "Server list and updates",
                Link = "https://example.com",
                Status = ComponentStatus.Operational,
                Order = 1,
                GroupId = 2,
                Enabled = true,
                CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc),
                StatusName = "Operational"
            }
        },
        News =
        {
            new ForumPost
            {
                Id = 11,
                Timestamp = 1767322800,
                Title = "Server moved",
                Author = "admin",
                Text = "The server has a new address.",
                Url = "https://example.com/news/11"
            }
        },
        ForumApiKey = "test-key"
    };

    [Fact]
    public async Task ServerList_UsesPascalCaseExceptIp()
    {
        await using var harness = await MasterApiHarness.StartAsync(BuildSettings());

        var json = JsonDocument.Parse(await harness.Client.GetStringAsync("/servers/detailed")).RootElement;

        Assert.Equal(JsonValueKind.Array, json.GetProperty("List").ValueKind);

        var entry = json.GetProperty("List")[0];

        Assert.True(entry.TryGetProperty("ServerName", out _));
        Assert.True(entry.TryGetProperty("MaxPlayers", out _));
        Assert.True(entry.TryGetProperty("CurrentPlayers", out _));
        Assert.True(entry.TryGetProperty("GameMode", out _));
        Assert.True(entry.TryGetProperty("Passworded", out _));
        Assert.True(entry.TryGetProperty("Fqdn", out _));
        Assert.True(entry.TryGetProperty("ServerVersion", out _));
        Assert.True(entry.TryGetProperty("Port", out _));

        // The only renamed field in the launcher's model.
        Assert.True(entry.TryGetProperty("ip", out var ip));
        Assert.False(entry.TryGetProperty("IpPortAddress", out _));
        Assert.Equal("127.0.0.1:22005", ip.GetString());
    }

    [Fact]
    public async Task VerifiedServers_ReturnsStringArray()
    {
        await using var harness = await MasterApiHarness.StartAsync(BuildSettings());

        var json = JsonDocument.Parse(await harness.Client.GetStringAsync("/servers/verified")).RootElement;

        Assert.Equal("127.0.0.1:22005", json.GetProperty("List")[0].GetString());
    }

    [Fact]
    public async Task Statistics_CountsVerifiedAndListedPlayers()
    {
        await using var harness = await MasterApiHarness.StartAsync(BuildSettings());

        var json = JsonDocument.Parse(await harness.Client.GetStringAsync("/stats/masterlist")).RootElement;

        Assert.Equal(1, json.GetProperty("TotalServersOnline").GetInt32());
        Assert.Equal(1, json.GetProperty("TotalVerifiedServersOnline").GetInt32());
        Assert.Equal(7, json.GetProperty("TotalPlayersOnline").GetInt32());
        Assert.Equal(7, json.GetProperty("TotalPlayersListedOnline").GetInt32());
    }

    [Fact]
    public async Task StatusComponents_UseLauncherSpellingsAndDateFormat()
    {
        await using var harness = await MasterApiHarness.StartAsync(BuildSettings());

        var json = JsonDocument.Parse(await harness.Client.GetStringAsync("/api/v1/components")).RootElement;

        // The launcher misspells this as "Oagination"; the correct spelling
        // deserializes into null and breaks the status view.
        Assert.True(json.TryGetProperty("Meta", out var meta));
        Assert.True(meta.TryGetProperty("Oagination", out var pagination));
        Assert.False(meta.TryGetProperty("Pagination", out _));

        Assert.True(pagination.TryGetProperty("per_page", out _));
        Assert.True(pagination.TryGetProperty("current_page", out _));
        Assert.True(pagination.TryGetProperty("total_pages", out _));

        var component = json.GetProperty("Data")[0];

        Assert.True(component.TryGetProperty("group_id", out _));
        Assert.False(component.TryGetProperty("GroupId", out _));
        Assert.True(component.TryGetProperty("status_name", out _));
        Assert.True(component.TryGetProperty("create_at", out _));
        Assert.True(component.TryGetProperty("updated_at", out _));

        // Not ISO 8601: the launcher pins "yyyy-MM-dd HH:mm:ss".
        Assert.Equal("2026-01-02 03:04:05", component.GetProperty("create_at").GetString());

        // Enum travels as a number, because the launcher has no string converter.
        Assert.Equal(JsonValueKind.Number, component.GetProperty("Status").ValueKind);
        Assert.Equal(1, component.GetProperty("Status").GetInt32());
    }

    [Fact]
    public async Task News_UsesUnixTimestampAndForumFieldNames()
    {
        await using var harness = await MasterApiHarness.StartAsync(BuildSettings());

        var json = JsonDocument.Parse(await harness.Client.GetStringAsync("/?news")).RootElement;
        var post = json[0];

        // Convert.ToInt64 runs on the raw value, so this must be a number.
        Assert.Equal(JsonValueKind.Number, post.GetProperty("post_date").ValueKind);
        Assert.Equal(1767322800, post.GetProperty("post_date").GetInt64());

        Assert.True(post.TryGetProperty("thread_id", out _));
        Assert.True(post.TryGetProperty("username", out _));
        Assert.True(post.TryGetProperty("message", out _));
        Assert.True(post.TryGetProperty("absolute_url", out _));
        Assert.True(post.TryGetProperty("Title", out _));
    }

    [Fact]
    public async Task TroubleshootingPost_RequiresTheConfiguredKey()
    {
        await using var harness = await MasterApiHarness.StartAsync(BuildSettings());

        var anonymous = await harness.Client.GetAsync("/?action=getPost&hash=wrong&value=11");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var authorised = await harness.Client.GetAsync("/?action=getPost&hash=test-key&value=11");
        Assert.Equal(System.Net.HttpStatusCode.OK, authorised.StatusCode);

        var missing = await harness.Client.GetAsync("/?action=getPost&hash=test-key&value=999");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task ForumEndpoint_IsClosedWhenNoKeyIsConfigured()
    {
        var settings = BuildSettings();
        settings.ForumApiKey = string.Empty;

        await using var harness = await MasterApiHarness.StartAsync(settings);

        var response = await harness.Client.GetAsync("/?action=getPost&hash=&value=11");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
