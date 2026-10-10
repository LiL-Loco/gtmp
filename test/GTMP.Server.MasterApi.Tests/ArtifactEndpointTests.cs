using System.Net;
using System.Security.Cryptography;

namespace GTMP.Server.MasterApi.Tests;

/// <summary>
/// Covers the update endpoints the launcher uses to fetch and verify payloads.
/// </summary>
public sealed class ArtifactEndpointTests
{
    [Fact]
    public async Task Version_IsServedAsPlainText()
    {
        await using var harness = await MasterApiHarness.StartAsync();
        harness.PublishVersion("stable", "client", "1.2.3");

        var response = await harness.Client.GetAsync("/update/stable/client/version");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1.2.3", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/update/stable/game/version")]
    [InlineData("/update/stable/cef/version")]
    [InlineData("/update/stable/launcher/version")]
    [InlineData("/update/stable/client/version")]
    [InlineData("/update/stable/client/hash")]
    [InlineData("/update/stable/client/files")]
    public async Task MissingArtifacts_ReturnNotFound(string url)
    {
        await using var harness = await MasterApiHarness.StartAsync();

        var response = await harness.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Hash_IsTheSha256OfThePublishedArchive()
    {
        await using var harness = await MasterApiHarness.StartAsync();

        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        harness.PublishArchive("stable", "client", payload);

        var expected = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var response = await harness.Client.GetAsync("/update/stable/client/hash");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Archive_IsServedWithItsOriginalBytes()
    {
        await using var harness = await MasterApiHarness.StartAsync();

        var payload = new byte[] { 9, 8, 7, 6 };
        harness.PublishArchive("stable", "launcher", payload);

        var response = await harness.Client.GetAsync("/update/stable/launcher/files");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Hash_ChangesWhenTheArchiveIsReplaced()
    {
        await using var harness = await MasterApiHarness.StartAsync();

        harness.PublishArchive("stable", "client", new byte[] { 1, 1, 1 });
        var first = await harness.Client.GetStringAsync("/update/stable/client/hash");

        harness.PublishArchive("stable", "client", new byte[] { 2, 2, 2 });
        var second = await harness.Client.GetStringAsync("/update/stable/client/hash");

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("../../appsettings")]
    [InlineData("..")]
    [InlineData("stable/../../secret")]
    [InlineData("stable%2F..%2F..")]
    [InlineData("sta ble")]
    public async Task Channel_TraversalIsRefused(string channel)
    {
        await using var harness = await MasterApiHarness.StartAsync();
        harness.PublishVersion("stable", "client", "1.0.0");

        var response = await harness.Client.GetAsync($"/update/{channel}/client/version");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChannelsAreIsolatedFromEachOther()
    {
        await using var harness = await MasterApiHarness.StartAsync();
        harness.PublishVersion("stable", "client", "1.0.0");

        var beta = await harness.Client.GetAsync("/update/beta/client/version");

        Assert.Equal(HttpStatusCode.NotFound, beta.StatusCode);
    }
}
