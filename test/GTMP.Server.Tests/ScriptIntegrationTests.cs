using GTMP.Server.Core;
using GTMP.Server.Core.Configuration;
using GTMP.Server.Core.Runtime;
using GTMP.Server.Core.Scripting;
using GTMP.Scripting.Hosting;
using GTMP.Shared;
using Xunit;

namespace GTMP.Server.Tests;

/// <summary>
/// Covers the seam between the script engine and the server: a game mode must be able
/// to call natives that mutate real server state.
/// </summary>
public sealed class ScriptIntegrationTests
{
    private static (ScriptTickService Scripts, ServerContext Context) CreateServer()
    {
        var settings = new ServerSettings { TickRate = 20 };
        var context = new ServerContext(settings, new InMemoryLogger());

        var natives = new ServerNatives(context);
        var engine = new ScriptEngine(natives.Registry);
        var scripts = new ScriptTickService(context, engine);

        return (scripts, context);
    }

    [Fact]
    public void OnScriptLoadCanSpawnEntitiesThroughNatives()
    {
        var (scripts, context) = CreateServer();
        var folder = WriteScript("spawn.pwn", """
            OnScriptLoad()
            {
                CreateVehicle("adder", 1.0, 2.0, 3.0);
                Log("done");
                return true;
            }
            """);

        var loaded = scripts.LoadResourceFolder(folder);

        Assert.Equal(1, loaded);
        Assert.Equal(0, scripts.ScriptErrors);

        var vehicles = context.Entities.OfType<VehicleProperties>().ToList();
        Assert.Single(vehicles);
        Assert.Equal("adder", vehicles[0].Model);
        Assert.Equal(1f, vehicles[0].Position.X, 3);
    }

    [Fact]
    public void GlobalsKeepStateAcrossTicks()
    {
        var (scripts, _) = CreateServer();
        var folder = WriteScript("counter.pwn", """
            new calls = 0;

            OnTick(tick)
            {
                calls = calls + 1;
                return true;
            }

            GetCalls()
            {
                return calls;
            }
            """);

        scripts.LoadResourceFolder(folder);

        for (var i = 0; i < 25; i++) scripts.Tick(i);

        var engine = (ScriptEngine)typeof(ScriptTickService)
            .GetField("_engine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(scripts)!;

        Assert.True(engine.TryInvoke("GetCalls", [], out var result, out var error));
        Assert.Null(error);

        // OnTick runs every tenth tick, so 25 ticks produce 2 callbacks.
        Assert.Equal(2.0, Convert.ToDouble(result), 3);
    }

    [Fact]
    public void ABrokenScriptIsReportedAndDoesNotStopTheServer()
    {
        var (scripts, _) = CreateServer();
        var folder = WriteScript("broken.pwn", """
            OnTick(tick)
            {
                return undefinedThing();
            }
            """);

        var loaded = scripts.LoadResourceFolder(folder);

        Assert.Equal(1, loaded);

        // Must neither throw nor report a load error; the failure surfaces per tick.
        for (var i = 0; i < 20; i++) scripts.Tick(i);

        Assert.True(scripts.ScriptErrors > 0);
    }

    [Fact]
    public void NativesValidateTheirArgumentsInsteadOfThrowing()
    {
        var (scripts, _) = CreateServer();
        var folder = WriteScript("badargs.pwn", """
            OnTick(tick)
            {
                GivePlayerMoney(9999, -1);
                SetEntityPosition("not a handle", 1.0, 2.0, 3.0);
                return true;
            }
            """);

        scripts.LoadResourceFolder(folder);

        for (var i = 0; i < 20; i++) scripts.Tick(i);

        Assert.Equal(0, scripts.ScriptErrors);
    }

    [Fact]
    public void AnInfiniteLoopInAScriptIsStoppedByTheBudget()
    {
        var (scripts, _) = CreateServer();
        var folder = WriteScript("hang.pwn", """
            OnTick(tick)
            {
                new i = 0;
                while (true)
                {
                    i = i + 1;
                }
                return true;
            }
            """);

        scripts.LoadResourceFolder(folder);

        // Must complete rather than hang the tick.
        for (var i = 0; i < 20; i++) scripts.Tick(i);

        Assert.True(scripts.ScriptErrors > 0);
    }

    [Fact]
    public void PlayerConnectReachesTheScriptWithTheAssignedHandle()
    {
        var (scripts, context) = CreateServer();
        var folder = WriteScript("join.pwn", """
            new lastJoin = "";

            OnPlayerConnect(playerId, name)
            {
                lastJoin = name;
                GivePlayerMoney(playerId, 1000);
                return true;
            }

            GetLastJoin()
            {
                return lastJoin;
            }
            """);

        scripts.LoadResourceFolder(folder);

        var player = new PlayerProperties { Name = "Carol" };
        var handle = context.Entities.Create(player);

        scripts.OnPlayerConnected(handle, "Carol");

        var engine = GetEngine(scripts);
        Assert.True(engine.TryInvoke("GetLastJoin", [], out var result, out _));
        Assert.Equal("Carol", result);
        Assert.Equal(1000, player.Money);
    }

    private static ScriptEngine GetEngine(ScriptTickService scripts) =>
        (ScriptEngine)typeof(ScriptTickService)
            .GetField("_engine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(scripts)!;

    private static string WriteScript(string fileName, string content)
    {
        var folder = Path.Combine(Path.GetTempPath(), "gtmp-script-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        File.WriteAllText(Path.Combine(folder, fileName), content);
        return folder;
    }
}