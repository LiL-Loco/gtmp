using GTMP.Server.MasterApi.Configuration;
using GTMP.Server.MasterApi.Contracts;
using GTMP.Server.MasterApi.Infrastructure;

namespace GTMP.Server.MasterApi.Endpoints;

/// <summary>
/// Maps every HTTP surface the GT-MP launcher talks to.
/// </summary>
/// <remarks>
/// Three logical services live in one process because the launcher is
/// configured with three base addresses and they all resolve to the same host
/// in a self-hosted setup. They differ by path, not by port.
/// </remarks>
public static class MasterApiEndpoints
{
    /// <summary>Registers all routes.</summary>
    public static void MapMasterApi(this WebApplication app)
    {
        MapServerList(app);
        MapStatistics(app);
        MapStatus(app);
        MapUpdates(app);
        MapForum(app);
    }

    private static void MapServerList(WebApplication app)
    {
        app.MapGet("/servers/detailed", (MasterApiSettings settings) =>
            Results.Json(new ServerListResponse { List = settings.Servers.ToList() }));

        app.MapGet("/servers/verified", (MasterApiSettings settings) =>
            Results.Json(new VerifiedServersResponse { List = settings.VerifiedServers.ToArray() }));
    }

    private static void MapStatistics(WebApplication app)
    {
        app.MapGet("/stats/masterlist", (MasterApiSettings settings) =>
        {
            var verified = settings.VerifiedServers
                .Select(address => string.Equals(address, string.Empty, StringComparison.OrdinalIgnoreCase) ? null : address)
                .Where(address => address is not null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var listed = settings.Servers.Where(server => verified.Contains(server.IpPortAddress)).ToList();

            return Results.Json(new StatisticsResponse
            {
                TotalServersOnline = settings.Servers.Count,
                TotalListedServersOnline = settings.Servers.Count,
                TotalVerifiedServersOnline = listed.Count,
                TotalPlayersOnline = settings.Servers.Sum(server => (int)server.CurrentPlayers),
                TotalPlayersListedOnline = listed.Sum(server => (int)server.CurrentPlayers)
            });
        });
    }

    private static void MapStatus(WebApplication app)
    {
        app.MapGet("/api/v1/components", (MasterApiSettings settings) =>
        {
            var components = settings.Components
                .OrderBy(component => component.Order)
                .ThenBy(component => component.Id)
                .ToList();

            return Results.Json(new StatusComponentsResponse
            {
                Meta = new StatusMeta
                {
                    Oagination = new Pagination
                    {
                        Total = components.Count,
                        Count = components.Count,
                        PerPage = 25,
                        CurrentPage = 1,
                        TotalPages = components.Count == 0 ? 0 : 1,
                        Links = new PaginationLinks { First = null, Last = null, Prev = null, Next = null }
                    }
                },
                Data = components
            });
        });
    }

    private static void MapUpdates(WebApplication app)
    {
        app.MapGet("/update/{channel}/game/version", (ArtifactStore artifacts, string channel) =>
            VersionResponse(artifacts.GetVersion(ArtifactKind.Game, channel)));

        app.MapGet("/update/{channel}/cef/version", (ArtifactStore artifacts, string channel) =>
            VersionResponse(artifacts.GetVersion(ArtifactKind.Cef, channel)));

        app.MapGet("/update/{channel}/launcher/version", (ArtifactStore artifacts, string channel) =>
            VersionResponse(artifacts.GetVersion(ArtifactKind.Launcher, channel)));

        app.MapGet("/update/{channel}/client/version", (ArtifactStore artifacts, string channel) =>
            VersionResponse(artifacts.GetVersion(ArtifactKind.Client, channel)));

        app.MapGet("/update/{channel}/launcher/hash", (ArtifactStore artifacts, string channel) =>
            HashResponse(artifacts.GetArchiveHash(ArtifactKind.Launcher, channel)));

        app.MapGet("/update/{channel}/client/hash", (ArtifactStore artifacts, string channel) =>
            HashResponse(artifacts.GetArchiveHash(ArtifactKind.Client, channel)));

        app.MapGet("/update/{channel}/launcher/files", (ArtifactStore artifacts, string channel) =>
            ArchiveResponse(artifacts.GetArchivePath(ArtifactKind.Launcher, channel)));

        app.MapGet("/update/{channel}/client/files", (ArtifactStore artifacts, string channel) =>
            ArchiveResponse(artifacts.GetArchivePath(ArtifactKind.Client, channel)));
    }

    private static void MapForum(WebApplication app)
    {
        // Both forum routes live on "/" because the launcher appends "?news" and
        // "?action=getPost&hash=...&value=..." to a base address it stores as-is.
        //
        // The query collection is read directly instead of relying on parameter
        // binding: the launcher sends a bare "?news" with no value at all, which
        // binds to null rather than to an empty string.
        app.MapGet("/", (HttpRequest request, MasterApiSettings settings) =>
        {
            var query = request.Query;

            if (query.ContainsKey("news"))
            {
                return Results.Json(settings.News
                    .OrderByDescending(post => post.Timestamp)
                    .ToList());
            }

            var action = query["action"].ToString();
            if (!string.Equals(action, "getPost", StringComparison.Ordinal))
            {
                return Results.NotFound();
            }

            if (!IsAuthorised(settings.ForumApiKey, query["hash"].ToString()))
            {
                return Results.Unauthorized();
            }

            if (!int.TryParse(query["value"].ToString(), out var threadId))
            {
                return Results.BadRequest();
            }

            var post = settings.News.FirstOrDefault(item => item.Id == threadId);
            return post is null ? Results.NotFound() : Results.Json(post);
        });
    }

    /// <summary>
    /// Compares the presented key without leaking its length or content through
    /// timing.
    /// </summary>
    private static bool IsAuthorised(string? expected, string? presented)
    {
        if (string.IsNullOrEmpty(expected)) return false;

        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        var presentedBytes = System.Text.Encoding.UTF8.GetBytes(presented ?? string.Empty);

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expectedBytes, presentedBytes);
    }

    private static IResult VersionResponse(string? version) =>
        version is null
            ? Results.NotFound()
            : Results.Text(version, "text/plain; charset=utf-8");

    private static IResult HashResponse(string? hash) =>
        hash is null
            ? Results.NotFound()
            : Results.Text(hash, "text/plain; charset=utf-8");

    private static IResult ArchiveResponse(string? archivePath) =>
        archivePath is null
            ? Results.NotFound()
            : Results.File(archivePath, "application/zip", enableRangeProcessing: true);
}
