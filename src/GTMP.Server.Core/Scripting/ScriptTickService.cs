using GTMP.Server.Core.Networking;
using GTMP.Server.Core.Runtime;
using GTMP.Scripting.Hosting;
using GTMP.Shared;

namespace GTMP.Server.Core.Scripting;

/// <summary>
/// Bridges the scripting engine into the tick loop: loads the resource scripts and
/// calls their callbacks.
/// </summary>
/// <remarks>
/// A script that throws is logged and skipped. The alternative - letting it escape -
/// would mean one broken game mode takes the whole server down on the next tick.
/// </remarks>
public sealed class ScriptTickService : IServerTick
{
    private readonly ScriptEngine _engine;
    private readonly ServerContext _context;
    private readonly NetworkService? _network;

    public ScriptTickService(ServerContext context, ScriptEngine engine, NetworkService? network = null)
    {
        _context = context;
        _engine = engine;
        _network = network;
    }

    /// <summary>Ticks that have elapsed since the last <c>OnTick</c> callback.</summary>
    private long _ticksSinceOnTick;

    /// <summary>How often <c>OnTick</c> runs. Position sync is far more often than game logic needs.</summary>
    public int TickCallbackInterval { get; init; } = 10;

    public int LoadedScriptCount { get; private set; }

    public int ScriptErrors { get; private set; }

    /// <summary>Loads every <c>.pwn</c> in the resource folder. Returns how many loaded.</summary>
    public int LoadResourceFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            _context.Logger.Warn($"Resource folder not found: {folderPath}");
            return 0;
        }

        var loaded = 0;

        foreach (var scriptPath in Directory.GetFiles(folderPath, "*.pwn", SearchOption.AllDirectories))
        {
            var result = _engine.Load(scriptPath);

            if (result.Success)
            {
                loaded++;
                _context.Logger.Info($"Loaded {result} from {result.Path}");
            }
            else
            {
                _context.Logger.Error($"Failed to load {result.Path}: {result.Error}");
            }
        }

        LoadedScriptCount = loaded;

        // OnScriptLoad runs once, after every file is in place, so a mode may safely
        // call functions that another resource defines.
        if (loaded > 0) Invoke("OnScriptLoad");

        return loaded;
    }

    public void Tick(long tickNumber)
    {
        _ticksSinceOnTick++;

        if (_ticksSinceOnTick < TickCallbackInterval) return;

        _ticksSinceOnTick = 0;

        Invoke("OnTick", [tickNumber]);
    }

    /// <summary>Invokes a script callback by convention, if it exists.</summary>
    public bool Invoke(string functionName, object?[]? args = null)
    {
        if (_engine.Interpreter.FindFunction(functionName) is null) return false;

        if (!_engine.TryInvoke(functionName, args, out _, out var error))
        {
            ScriptErrors++;
            _context.Logger.Error($"Script error in '{functionName}': {error?.Message}");
            return false;
        }

        return true;
    }

    /// <summary>Convenience hooks the network layer calls.</summary>
    public void OnPlayerConnected(NetHandle handle, string name) =>
        Invoke("OnPlayerConnect", [(double)handle.Id, name]);

    public void OnPlayerDisconnected(NetHandle handle) =>
        Invoke("OnPlayerDisconnect", [(double)handle.Id]);

    public void OnPlayerChat(NetHandle handle, string name, string text) =>
        Invoke("OnPlayerChat", [(double)handle.Id, name, text]);
}