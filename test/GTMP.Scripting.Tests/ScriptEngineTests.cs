using GTMP.Scripting.Hosting;
using GTMP.Scripting.Runtime;

namespace GTMP.Scripting.Tests;

/// <summary>Covers loading, invoking and the re-entrancy guard of the engine.</summary>
public sealed class ScriptEngineTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "gtmp-scripting-tests", Guid.NewGuid().ToString("N"));

    public ScriptEngineTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void ScriptEngine_LoadsAndInvokesScriptFunction()
    {
        string path = Write("gamemode.pwn", "GetName() { return \"Test\"; }");
        ScriptEngine engine = new(new NativeFunctionRegistry());

        ScriptLoadResult load = engine.Load(path);
        Assert.True(load.Success, load.Error);
        Assert.Equal("gamemode.pwn", load.ScriptName);
        Assert.Equal(1, load.FunctionCount);

        Assert.True(engine.TryInvoke("GetName", null, out object? result, out ScriptRuntimeException? error));
        Assert.Null(error);
        Assert.Equal("Test", result);
    }

    [Fact]
    public void ScriptEngine_ReportsMissingFileWithoutThrowing()
    {
        ScriptEngine engine = new(new NativeFunctionRegistry());

        ScriptLoadResult load = engine.Load(Path.Combine(_directory, "gibt-es-nicht.pwn"));

        Assert.False(load.Success);
        Assert.Contains("konnte nicht gelesen werden", load.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptEngine_ReportsSyntaxErrorWithPosition()
    {
        string path = Write("broken.pwn", "Bad()\n{\n    new x = 1\n    return x;\n}");
        ScriptEngine engine = new(new NativeFunctionRegistry());

        ScriptLoadResult load = engine.Load(path);

        Assert.False(load.Success);
        Assert.Equal(4, load.ErrorLine);
        Assert.Contains("';'", load.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptEngine_RejectsDuplicateFunctionInOneScript()
    {
        string path = Write("dup.pwn", "A() { return 1; }\nA() { return 2; }");
        ScriptEngine engine = new(new NativeFunctionRegistry());

        ScriptLoadResult load = engine.Load(path);

        Assert.False(load.Success);
        Assert.Contains("doppelt deklariert", load.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptEngine_RuntimeErrorNamesTheScriptFile()
    {
        string path = Write("gamemode.pwn", @"
Crash()
{
    new arr = [1];
    return arr[7];
}");
        ScriptEngine engine = new(new NativeFunctionRegistry());
        engine.Load(path);

        Assert.False(engine.TryInvoke("Crash", null, out object? result, out ScriptRuntimeException? error));

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Equal("gamemode.pwn", error!.ScriptName);
        Assert.Equal(5, error.Line);
    }

    [Fact]
    public void ScriptEngine_TryInvokeReportsUnknownFunction()
    {
        ScriptEngine engine = new(new NativeFunctionRegistry());

        Assert.False(engine.TryInvoke("DoesNotExist", null, out object? result, out ScriptRuntimeException? error));
        Assert.Null(result);
        Assert.Contains("Unbekannte Funktion 'DoesNotExist'", error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptEngine_RefusesReloadWhileScriptIsExecuting()
    {
        string path = Write("reload.pwn", "Trigger() { return Reload(); }");
        NativeFunctionRegistry natives = new();
        ScriptEngine engine = new(natives);

        ScriptLoadResult first = engine.Load(path);
        Assert.True(first.Success, first.Error);

        ScriptLoadResult? duringExecution = null;
        natives.Register("Reload", _ =>
        {
            duringExecution = engine.Load(path);
            return 1d;
        });

        Assert.True(engine.TryInvoke("Trigger", null, out object? result, out ScriptRuntimeException? error));
        Assert.Null(error);
        Assert.Equal(1d, result);

        Assert.NotNull(duringExecution);
        Assert.False(duringExecution!.Success);
        Assert.Contains("wird gerade ausgeführt", duringExecution.Error!, StringComparison.Ordinal);

        // After the call unwound the engine is usable again.
        Assert.True(engine.Load(path).Success);
    }

    [Fact]
    public void ScriptEngine_KeepsTwoEnginesIndependent()
    {
        string path = Write("shared.pwn", "Value() { return 1; }");
        ScriptEngine first = new(new NativeFunctionRegistry());
        ScriptEngine second = new(new NativeFunctionRegistry());

        first.Load(path);

        Assert.True(first.TryInvoke("Value", null, out object? firstResult, out _));
        Assert.Equal(1d, firstResult);

        Assert.False(second.TryInvoke("Value", null, out _, out ScriptRuntimeException? error));
        Assert.Contains("Unbekannte Funktion", error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptEngine_ReloadReplacesFunctionsOfTheSameFile()
    {
        NativeFunctionRegistry natives = new();
        ScriptEngine engine = new(natives);

        engine.Load(Write("mode.pwn", "Old() { return 1; }"));
        Assert.True(engine.TryInvoke("Old", null, out _, out _));

        engine.Load(Write("mode.pwn", "New() { return 2; }"));

        Assert.False(engine.TryInvoke("Old", null, out _, out ScriptRuntimeException? error));
        Assert.Contains("Unbekannte Funktion 'Old'", error!.Message, StringComparison.Ordinal);
        Assert.True(engine.TryInvoke("New", null, out object? result, out _));
        Assert.Equal(2d, result);
    }

    [Fact]
    public void ScriptEngine_PassesArgumentsFromTheHostIntoTheScript()
    {
        string path = Write("args.pwn", "Scale(value, factor) { return value * factor; }");
        ScriptEngine engine = new(new NativeFunctionRegistry());
        engine.Load(path);

        Assert.True(engine.TryInvoke("Scale", [3d, 7d], out object? result, out ScriptRuntimeException? error));

        Assert.Null(error);
        Assert.Equal(21d, result);
    }

    private string Write(string name, string source)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, source);
        return path;
    }
}
