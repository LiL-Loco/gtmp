using System.Net.Sockets;
using System.Text.Json;
using GTMP.Server.Core;
using GTMP.Server.Core.Configuration;
using GTMP.Server.Core.Networking;
using GTMP.Server.Core.Runtime;
using GTMP.Server.Core.Scripting;
using GTMP.Scripting.Hosting;

namespace GTMP.Server.Host;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static bool _shutdownRequested;

    private static int Main(string[] args)
    {
        var commandLine = CommandLine.Parse(args);

        var baseDirectory = AppContext.BaseDirectory;
        var settings = LoadSettings(baseDirectory);
        var logger = new ConsoleLogger();

        if (commandLine.Port.HasValue) settings.Port = commandLine.Port.Value;
        if (commandLine.TickRate.HasValue) settings.TickRate = commandLine.TickRate.Value;

        try
        {
            settings.Validate();
        }
        catch (Exception exception)
        {
            logger.Error("Server configuration is invalid.", exception);
            return 1;
        }

        var context = new ServerContext(settings, logger);

        var network = new NetworkService(context);
        var replication = new EntityReplicationService(context, network);
        network.SetFullSyncHandler(replication.SendFullSyncTo);

        var natives = new ServerNatives(context, network);
        var scriptEngine = new ScriptEngine(natives.Registry);
        var scripts = new ScriptTickService(context, scriptEngine);

        network.PlayerConnected += session => scripts.OnPlayerConnected(session.PlayerHandle, session.Name);
        network.PlayerDisconnected += session => scripts.OnPlayerDisconnected(session.PlayerHandle);

        // Player chat reaches the script first; the default behaviour is to pass it on,
        // so a game mode that does not care about chat still behaves like a chat server.
        network.PlayerChat += (session, text) =>
        {
            if (!scripts.OnPlayerChat(session.PlayerHandle, session.Name, text))
                network.BroadcastChat(session.PlayerHandle, session.Name, text);
        };

        var resourceFolder = Path.Combine(baseDirectory, settings.ResourceFolder);
        var loadedScripts = scripts.LoadResourceFolder(resourceFolder);
        logger.Info($"Loaded {loadedScripts} script(s) from {resourceFolder}.");

        var tickLoop = new TickLoop
        {
            Settings = settings,
            ShouldContinue = () => !_shutdownRequested
        };

        tickLoop.Register(new PumpInboundTick(network));
        tickLoop.Register(scripts);
        tickLoop.Register(replication);
        tickLoop.Register(new HeartbeatTick(context, network, replication, scripts));

        // Ctrl+C has to be handled before the loop starts, otherwise a long blocking
        // sleep swallows the signal and the server looks hung.
        using var shutdownSignal = new ManualResetEventSlim(false);

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            _shutdownRequested = true;
            shutdownSignal.Set();
        };

        try
        {
            network.Start();
        }
        catch (SocketException exception)
        {
            logger.Error($"Could not bind UDP port {settings.Port}. Is another instance running?", exception);
            return 1;
        }

        logger.Info($"{settings.ServerName} listening on UDP {settings.Port} ({settings.TicksPerSecond} ticks/s).");

        var loopTask = Task.Run(() => tickLoop.Run());

        if (commandLine.RunForSeconds.HasValue)
        {
            // Bounded run so the lifecycle can be exercised from a script or a
            // container health check without sending a console signal.
            var budget = TimeSpan.FromSeconds(commandLine.RunForSeconds.Value);

            if (!shutdownSignal.Wait(budget))
            {
                logger.Info($"Run time of {budget.TotalSeconds:F0}s reached, shutting down.");
                _shutdownRequested = true;
            }
        }
        else
        {
            shutdownSignal.Wait();
        }

        loopTask.Wait(TimeSpan.FromSeconds(5));

        logger.Info($"Stopped after {context.Uptime.TotalSeconds:F0}s and {tickLoop.TickNumber} ticks.");
        logger.Info($"{context.Entities.Count} entities, {context.PlayerCount} players.");
        logger.Info($"Replicated {replication.UpdatesSent} updates and {replication.DeletesSent} deletes.");

        network.Dispose();

        return 0;
    }

    /// <summary>Drains the receive queue on the tick thread.</summary>
    private sealed class PumpInboundTick(NetworkService network) : IServerTick
    {
        public void Tick(long tickNumber) => network.PumpInbound();
    }

    private static ServerSettings LoadSettings(string baseDirectory)
    {
        var settingsPath = Path.Combine(baseDirectory, "server.json");

        if (!File.Exists(settingsPath))
        {
            var defaults = new ServerSettings();
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(defaults, JsonOptions));
            return defaults;
        }

        return JsonSerializer.Deserialize<ServerSettings>(
                   File.ReadAllText(settingsPath), JsonOptions)
               ?? new ServerSettings();
    }

    /// <summary>Writes a periodic status line so the process is observably alive.</summary>
    private sealed class HeartbeatTick(
        ServerContext context,
        NetworkService network,
        EntityReplicationService replication,
        ScriptTickService scripts) : IServerTick
    {
        private const int ReportEveryTicks = 200;

        public void Tick(long tickNumber)
        {
            context.TickNumber = tickNumber;

            if (tickNumber % ReportEveryTicks != 0) return;

            context.Logger.Info(
                $"tick {tickNumber} | uptime {context.Uptime.TotalSeconds:F0}s | " +
                $"players {context.PlayerCount} | entities {context.Entities.Count} | " +
                $"sessions {network.SessionCount} | drops {network.DroppedPackets} | " +
                $"repl {replication.UpdatesSent}u/{replication.DeletesSent}d | scripts {scripts.LoadedScriptCount}/{scripts.ScriptErrors}err");
        }
    }
}

internal sealed class ConsoleLogger : IServerLogger
{
    public void Info(string message) => Console.WriteLine($"[INFO ] {message}");

    public void Warn(string message) => Console.WriteLine($"[WARN ] {message}");

    public void Error(string message, Exception? exception = null) =>
        Console.Error.WriteLine($"[ERROR] {message}{(exception is null ? string.Empty : Environment.NewLine + exception)}");
}

/// <summary>Parsed command line. Unknown arguments are rejected rather than ignored.</summary>
internal sealed record CommandLine(int? Port, int? TickRate, int? RunForSeconds)
{
    public static CommandLine Parse(string[] args)
    {
        int? port = null, tickRate = null, runFor = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length:
                    port = int.Parse(args[++i]);
                    break;

                case "--tick-rate" when i + 1 < args.Length:
                    tickRate = int.Parse(args[++i]);
                    break;

                case "--run-for" when i + 1 < args.Length:
                    runFor = int.Parse(args[++i]);
                    break;

                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
            }
        }

        return new CommandLine(port, tickRate, runFor);
    }
}