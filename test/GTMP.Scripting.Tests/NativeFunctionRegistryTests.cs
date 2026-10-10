using GTMP.Scripting.Hosting;
using GTMP.Scripting.Language;
using GTMP.Scripting.Runtime;

namespace GTMP.Scripting.Tests;

/// <summary>Covers native registration and the error behaviour of unknown or misused natives.</summary>
public sealed class NativeFunctionRegistryTests
{
    [Fact]
    public void Registry_RegistersAndInvokesNative()
    {
        NativeFunctionRegistry registry = new();
        registry.Register("Server.GiveMoney", 2, args => ScriptValue.ToNumber(args[0], "t") + ScriptValue.ToNumber(args[1], "t"));

        Assert.True(registry.IsRegistered("Server.GiveMoney"));
        Assert.Equal(1, registry.Count);
        Assert.Equal(30d, registry.Invoke("Server.GiveMoney", [10d, 20d]));
    }

    [Fact]
    public void Registry_ThrowsOnDuplicateRegistration()
    {
        NativeFunctionRegistry registry = new();
        registry.Register("Ping", _ => 1d);

        ScriptSyntaxException error = Assert.Throws<ScriptSyntaxException>(() => registry.Register("Ping", _ => 2d));

        Assert.Contains("bereits registriert", error.Message, StringComparison.Ordinal);
        Assert.Contains("Ping", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_ThrowsOnInvalidArgumentRange()
    {
        NativeFunctionRegistry registry = new();

        ScriptSyntaxException error = Assert.Throws<ScriptSyntaxException>(() => registry.Register("Broken", 3, 1, _ => 0d));

        Assert.Contains("Broken", error.Message, StringComparison.Ordinal);
        Assert.Contains("Argumentbereich", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_ThrowsNamingUnknownNative()
    {
        NativeFunctionRegistry registry = new();

        ScriptRuntimeException error = Assert.Throws<ScriptRuntimeException>(() => registry.Invoke("Nope", []));

        Assert.Contains("Unbekannte Funktion 'Nope'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_ThrowsOnArgumentCountMismatchAtCallTime()
    {
        NativeFunctionRegistry registry = new();
        registry.Register("Exactly", 2, _ => 1d);

        ScriptRuntimeException error = Assert.Throws<ScriptRuntimeException>(() => registry.Invoke("Exactly", [1d]));

        Assert.Contains("erwartet 2 Argument(e)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_SupportsVariadicNatives()
    {
        NativeFunctionRegistry registry = new();
        registry.Register("Sum", args =>
        {
            double total = 0d;
            foreach (object? argument in args)
            {
                total += ScriptValue.ToNumber(argument, "t");
            }

            return total;
        });

        Assert.Equal(6d, registry.Invoke("Sum", [1d, 2d, 3d]));
    }

    [Fact]
    public void Registry_MakesUnknownNativeVisibleAsRuntimeError()
    {
        ScriptRuntimeException error = Assert.Throws<ScriptRuntimeException>(
            () => ScriptFactory.Run("main() { return Missing.Native(); }"));

        Assert.Contains("Missing.Native", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptValue_FormatsValuesForHostLogging()
    {
        Assert.Equal("3", ScriptValue.ToDisplayString(3d));
        Assert.Equal("3.5", ScriptValue.ToDisplayString(3.5d));
        Assert.Equal("null", ScriptValue.ToDisplayString(null));
        Assert.Equal("true", ScriptValue.ToDisplayString(true));
        Assert.Equal("Array(2)", ScriptValue.ToDisplayString(new object?[] { 1d, 2d }));
    }

    [Fact]
    public void ScriptValue_AppliesPawnTruthiness()
    {
        Assert.False(ScriptValue.IsTruthy(0d));
        Assert.False(ScriptValue.IsTruthy(null));
        Assert.False(ScriptValue.IsTruthy(string.Empty));
        Assert.False(ScriptValue.IsTruthy(new object?[0]));
        Assert.True(ScriptValue.IsTruthy(0.5d));
        Assert.True(ScriptValue.IsTruthy("x"));
    }
}
