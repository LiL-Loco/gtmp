using GTMP.Scripting.Hosting;
using GTMP.Scripting.Language;
using GTMP.Scripting.Runtime;

namespace GTMP.Scripting.Tests;

/// <summary>
/// Helpers that turn a source string into a running interpreter or engine, so the tests can stay
/// focused on behaviour instead of on setup.
/// </summary>
internal static class ScriptFactory
{
    /// <summary>Name used for scripts that are not loaded from disk.</summary>
    internal const string DefaultScriptName = "test.pwn";

    /// <summary>
    /// Builds an interpreter with all functions of the given source registered.
    /// </summary>
    /// <param name="source">The <c>.pwn</c> source.</param>
    /// <param name="scriptName">Name used in runtime error messages.</param>
    /// <param name="configure">Optional native registrations.</param>
    /// <returns>The interpreter.</returns>
    internal static Interpreter CreateInterpreter(string source, string scriptName = DefaultScriptName, Action<NativeFunctionRegistry>? configure = null)
    {
        NativeFunctionRegistry natives = new();
        configure?.Invoke(natives);

        Interpreter interpreter = new(natives);
        foreach (FunctionDeclaration declaration in Parser.ParseSource(source, scriptName))
        {
            interpreter.Register(new ScriptFunction(declaration, scriptName));
        }

        return interpreter;
    }

    /// <summary>
    /// Runs the <c>main</c> function of an in-memory script.
    /// </summary>
    /// <param name="source">The <c>.pwn</c> source.</param>
    /// <param name="configure">Optional native registrations.</param>
    /// <returns>The return value of <c>main</c>.</returns>
    internal static object? Run(string source, Action<NativeFunctionRegistry>? configure = null)
        => CreateInterpreter(source, DefaultScriptName, configure).Call("main");
}
