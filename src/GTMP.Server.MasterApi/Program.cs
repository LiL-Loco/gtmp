using System.Text.Json;
using System.Text.Json.Serialization;
using GTMP.Server.MasterApi.Configuration;
using GTMP.Server.MasterApi.Endpoints;
using GTMP.Server.MasterApi.Infrastructure;

namespace GTMP.Server.MasterApi;

/// <summary>
/// Host for the master, status and forum HTTP surfaces the GT-MP launcher calls.
/// </summary>
public static class Program
{
    /// <summary>Port used when none is given on the command line.</summary>
    public const int DefaultPort = 4500;

    /// <summary>Entry point.</summary>
    public static void Main(string[] args)
    {
        var app = Build(args);
        app.Run();
    }

    /// <summary>
    /// Builds the configured application without starting it, so tests can bind
    /// it to an ephemeral port and drive it over real HTTP.
    /// </summary>
    /// <param name="args">Command line arguments. <c>--port</c> is honoured; 0 binds an ephemeral port.</param>
    /// <param name="settingsOverride">Settings to use instead of reading <c>masterapi.json</c>.</param>
    /// <param name="artifactDirectoryOverride">Artifact root to use instead of the configured relative directory.</param>
    public static WebApplication Build(
        string[] args,
        MasterApiSettings? settingsOverride = null,
        string? artifactDirectoryOverride = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        var port = ReadPort(args) ?? DefaultPort;
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

        // Minimal APIs default to camelCase, which the launcher does not read:
        // it deserializes with Newtonsoft and default settings, where the
        // PascalCase property names are the contract.
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = null;
            options.SerializerOptions.DictionaryKeyPolicy = null;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        });

        var settings = settingsOverride ?? LoadSettings();

        var configuredDirectory = artifactDirectoryOverride ?? settings.ArtifactDirectory;
        var artifactDirectory = Path.IsPathRooted(configuredDirectory)
            ? configuredDirectory
            : Path.Combine(AppContext.BaseDirectory, configuredDirectory);

        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(new ArtifactStore(artifactDirectory));

        var app = builder.Build();

        ReportConfigurationProblems(settings);

        app.MapMasterApi();

        return app;
    }

    /// <summary>
    /// Logs every entry that would be served in a broken state.
    /// </summary>
    /// <remarks>
    /// Deliberately non fatal. Refusing to start turns a typo in a data file into
    /// a master server outage, and the reason is printed right here.
    /// </remarks>
    private static void ReportConfigurationProblems(MasterApiSettings settings)
    {
        var problems = settings.Validate();

        foreach (var problem in problems)
        {
            Console.Error.WriteLine($"Configuration problem in {MasterApiSettings.FileName}: {problem}");
        }

        if (problems.Count > 0)
        {
            Console.Error.WriteLine(
                $"{problems.Count} configuration problem(s). The launcher will show incomplete data until they are fixed.");
        }
    }

    private static int? ReadPort(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var isPortFlag = args[index] is "--port" or "-p";
            if (!isPortFlag) continue;

            if (index + 1 >= args.Length) return null;
            if (!int.TryParse(args[index + 1], out var port) || port is < 0 or > 65535) return null;

            return port;
        }

        return null;
    }

    /// <summary>
    /// Reads <c>masterapi.json</c>, writing a documented template on first run.
    /// Never throws: a broken file must not stop the server from coming up.
    /// </summary>
    private static MasterApiSettings LoadSettings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, MasterApiSettings.FileName);

        try
        {
            if (File.Exists(path))
            {
                var content = File.ReadAllText(path);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    return JsonSerializer.Deserialize<MasterApiSettings>(content, JsonOptions) ?? new MasterApiSettings();
                }
            }

            var template = new MasterApiSettings();
            File.WriteAllText(path, JsonSerializer.Serialize(template, JsonOptions));

            return template;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Could not read {MasterApiSettings.FileName}, starting with defaults: {ex.Message}");

            return new MasterApiSettings();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };
}
