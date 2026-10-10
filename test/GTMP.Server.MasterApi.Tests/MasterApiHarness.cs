using Microsoft.AspNetCore.Builder;
using GTMP.Server.MasterApi;
using GTMP.Server.MasterApi.Configuration;
namespace GTMP.Server.MasterApi.Tests;

/// <summary>
/// Boots the real host on an ephemeral port and talks to it over HTTP.
/// </summary>
/// <remarks>
/// The endpoints are exercised through the actual pipeline on purpose: route
/// patterns, JSON casing and status codes are exactly what the launcher sees,
/// and none of it is covered by calling the handlers directly.
/// </remarks>
public sealed class MasterApiHarness : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly string _artifactDirectory;

    private MasterApiHarness(WebApplication app, HttpClient client, string artifactDirectory)
    {
        _app = app;
        _artifactDirectory = artifactDirectory;
        Client = client;
    }

    /// <summary>Client bound to the running instance.</summary>
    public HttpClient Client { get; }

    /// <summary>Root of the temporary artifact directory backing this instance.</summary>
    public string ArtifactDirectory => _artifactDirectory;

    /// <summary>Starts an instance with the given settings.</summary>
    public static async Task<MasterApiHarness> StartAsync(MasterApiSettings? settings = null)
    {
        var artifactDirectory = Path.Combine(Path.GetTempPath(), "gtmp-masterapi-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifactDirectory);

        var app = Program.Build(new[] { "--port", "0" }, settings ?? new MasterApiSettings(), artifactDirectory);

        await app.StartAsync();

        // Port 0 binds an ephemeral port; the real one is only known after start.
        var address = app.Urls.First().Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal);
        var client = new HttpClient { BaseAddress = new Uri(address) };

        return new MasterApiHarness(app, client, artifactDirectory);
    }

    /// <summary>Writes a version file for a channel and kind.</summary>
    public void PublishVersion(string channel, string kind, string version)
    {
        var directory = Path.Combine(ArtifactDirectory, "update", channel, kind);
        Directory.CreateDirectory(directory);

        File.WriteAllText(Path.Combine(directory, "version"), version);
    }

    /// <summary>Writes an update archive for a channel and kind.</summary>
    public string PublishArchive(string channel, string kind, byte[] content)
    {
        var directory = Path.Combine(ArtifactDirectory, "update", channel, kind);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "files.zip");
        File.WriteAllBytes(path, content);

        return path;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

        await _app.StopAsync();
        await _app.DisposeAsync();

        try
        {
            if (Directory.Exists(_artifactDirectory))
            {
                Directory.Delete(_artifactDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
