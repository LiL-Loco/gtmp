using System.Numerics;
using GTMP.Client;
using GTMP.Shared;

namespace GTMP.ClientHost;

/// <summary>
/// Console front end for <see cref="GameClient"/>.
/// </summary>
/// <remarks>
/// Commands are read from stdin on a background thread while the world state is
/// printed on the main thread, so the view stays responsive without pulling in a UI
/// framework.
/// </remarks>
internal static class Program
{
    private static GameClient? _client;

    private static int Main(string[] args)
    {
        var options = CommandLine.Parse(args);

        if (options.QuitImmediately)
        {
            PrintUsage();
            return 0;
        }

        using var client = new GameClient(options.Host, options.Port, options.Name);

        var result = client.Connect(options.Password, options.ConnectTimeoutMs);

        Console.WriteLine(result == ConnectResult.Accepted
            ? $"Connected to \"{client.ServerName}\" as player #{client.PlayerHandleId} " +
              $"({client.TicksPerSecond} ticks/s, max {client.MaxPlayers} players)."
            : $"Connection failed: {result}");

        if (result != ConnectResult.Accepted) return 1;

        _client = client;

        var reader = new Thread(ReadCommands) { IsBackground = true };
        reader.Start();

        PrintLoop(client, options.ViewIntervalMs);

        return 0;
    }

    private static void ReadCommands()
    {
        while (_client is { IsConnected: true })
        {
            var input = Console.ReadLine();

            if (input is null) return;

            input = input.Trim();
            if (input.Length == 0) continue;

            if (input == "quit" || input == "exit") return;

            if (input.StartsWith("say ", StringComparison.OrdinalIgnoreCase))
            {
                _client.SendChat(input[4..]);
                continue;
            }

            if (input.StartsWith("move ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = input[4..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 3
                    && float.TryParse(parts[0], out var x)
                    && float.TryParse(parts[1], out var y)
                    && float.TryParse(parts[2], out var z))
                {
                    _client.Move(new Vector3(x, y, z));
                    continue;
                }

                Console.WriteLine("usage: move <x> <y> <z>");
                continue;
            }

            if (input == "help") PrintUsage();
        }
    }

    private static void PrintLoop(GameClient client, int intervalMs)
    {
        Console.WriteLine("Type 'help' for commands.");

        while (client.IsConnected)
        {
            client.Pump();
            PrintWorld(client);

            Thread.Sleep(intervalMs);
        }
    }

    private static void PrintWorld(GameClient client)
    {
        var entities = client.Entities.OrderBy(e => e.Handle.Id).ToList();

        Console.WriteLine();
        Console.WriteLine($"--- {entities.Count} entities, {client.Players.Count} players ---");

        foreach (var entity in entities.Take(20))
        {
            var name = entity is PlayerProperties p ? $" \"{p.Name}\"" : string.Empty;
            Console.WriteLine(
                $"  {entity.Handle} {entity.Model}{name} @ " +
                $"{entity.Position.X:F1}, {entity.Position.Y:F1}, {entity.Position.Z:F1}");
        }

        foreach (var line in client.ChatLog.TakeLast(5))
            Console.WriteLine($"  {line}");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            GTMP reference client

              --host <host>        Server address (default 127.0.0.1)
              --port <port>        Server port (default 4499)
              --name <name>        Player name (default player)
              --password <pass>    Server password
              --interval <ms>      View refresh interval (default 1000)

            Commands while connected:
              move <x> <y> <z>     Teleport this player
              say <text>           Send chat
              help                 Show this text
              quit                 Disconnect
            """);
    }
}

internal sealed record CommandLine(
    string Host,
    int Port,
    string Name,
    string Password,
    int ConnectTimeoutMs,
    int ViewIntervalMs,
    bool QuitImmediately)
{
    public static CommandLine Parse(string[] args)
    {
        string host = "127.0.0.1";
        int port = 4499;
        string name = Environment.UserName.Length > 0 ? Environment.UserName : "player";
        string password = string.Empty;
        int connectTimeout = 5000;
        int interval = 1000;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--host" when i + 1 < args.Length:
                    host = args[++i];
                    break;

                case "--port" when i + 1 < args.Length:
                    port = int.Parse(args[++i]);
                    break;

                case "--name" when i + 1 < args.Length:
                    name = args[++i];
                    break;

                case "--password" when i + 1 < args.Length:
                    password = args[++i];
                    break;

                case "--connect-timeout" when i + 1 < args.Length:
                    connectTimeout = int.Parse(args[++i]);
                    break;

                case "--interval" when i + 1 < args.Length:
                    interval = int.Parse(args[++i]);
                    break;

                case "--help" or "-h":
                    return new CommandLine(host, port, name, password, connectTimeout, interval, true);

                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
            }
        }

        return new CommandLine(host, port, name, password, connectTimeout, interval, false);
    }
}