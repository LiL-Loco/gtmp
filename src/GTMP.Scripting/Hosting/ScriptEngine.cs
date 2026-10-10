using GTMP.Scripting.Language;
using GTMP.Scripting.Runtime;

namespace GTMP.Scripting.Hosting;

/// <summary>
/// Entry point of the scripting engine: loads <c>.pwn</c> files and calls into their functions.
/// </summary>
/// <remarks>
/// <para>
/// Single threaded by contract. The engine keeps no static state, so several engines can coexist
/// in one process, but a given instance must only be touched from the thread that runs the server
/// tick - that is what lets the reload guard and the step budget stay lock free.
/// </para>
/// <para>
/// Reloading a script while one of its functions is still on the stack is refused: the running
/// frame would keep executing the previous AST while the name table already points at the new
/// version, which produces failures that are almost impossible to reproduce.
/// </para>
/// </remarks>
public sealed class ScriptEngine
{
    private readonly Dictionary<string, int> _executingCounts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates an engine over a native registry.
    /// </summary>
    /// <param name="natives">Registry the loaded scripts may call into.</param>
    /// <param name="stepBudget">Steps a single top level invocation may execute.</param>
    /// <param name="maxCallDepth">Maximum nesting of script function calls.</param>
    /// <exception cref="ArgumentNullException"><paramref name="natives"/> is null.</exception>
    public ScriptEngine(NativeFunctionRegistry natives, int stepBudget = Interpreter.DefaultStepBudget, int maxCallDepth = Interpreter.DefaultMaxCallDepth)
    {
        ArgumentNullException.ThrowIfNull(natives);
        Natives = natives;
        Interpreter = new Interpreter(natives, stepBudget, maxCallDepth);
    }

    /// <summary>The native registry scripts may call into.</summary>
    public NativeFunctionRegistry Natives { get; }

    /// <summary>The interpreter doing the actual work.</summary>
    public Interpreter Interpreter { get; }

    /// <summary>Names of every script function currently callable.</summary>
    public IEnumerable<string> FunctionNames => Interpreter.FunctionNames;

    /// <summary>
    /// Reads, lexes and parses a <c>.pwn</c> file and registers its functions.
    /// </summary>
    /// <param name="path">Path to the script file.</param>
    /// <returns>
    /// A result describing either the successful load or the reason for the failure. IO problems
    /// are reported through the result, never as a thrown <see cref="System.IO.IOException"/>.
    /// </returns>
    public ScriptLoadResult Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ScriptLoadResult.Fail(path ?? string.Empty, "Der Pfad des Skripts darf nicht leer sein.");
        }

        string scriptName = Path.GetFileName(path);

        if (IsExecuting(scriptName))
        {
            return ScriptLoadResult.Fail(path, $"Das Skript '{scriptName}' wird gerade ausgeführt und kann nicht neu geladen werden.");
        }

        string source;
        try
        {
            source = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return ScriptLoadResult.Fail(path, $"Skript '{scriptName}' konnte nicht gelesen werden: {ex.Message}");
        }

        ParsedProgram program;
        try
        {
            Lexer lexer = new(source, scriptName);
            program = new Parser(lexer.Tokenize(), scriptName).ParseTopLevel();
        }
        catch (ScriptSyntaxException ex)
        {
            return ScriptLoadResult.Fail(path, ex.Message, ex.Line, ex.Column);
        }

        IReadOnlyList<FunctionDeclaration> declarations = program.Functions;

        HashSet<string> declared = new(StringComparer.Ordinal);
        foreach (FunctionDeclaration declaration in declarations)
        {
            if (!declared.Add(declaration.Name))
            {
                return ScriptLoadResult.Fail(
                    path,
                    $"Die Funktion '{declaration.Name}' ist in '{scriptName}' doppelt deklariert.",
                    declaration.Line,
                    declaration.Column);
            }
        }

        // Drop the previous version of this file first so a removed function really disappears.
        Interpreter.RemoveFunctionsFrom(scriptName);

        foreach (FunctionDeclaration declaration in declarations)
        {
            Interpreter.Register(new ScriptFunction(declaration, scriptName), replaceExisting: true);
        }

        // Globals last: a failing initializer must not leave half the script loaded.
        try
        {
            Interpreter.DefineGlobals(program.Globals, scriptName);
        }
        catch (ScriptRuntimeException ex)
        {
            return ScriptLoadResult.Fail(path, ex.Message, ex.Line, ex.Column);
        }

        return ScriptLoadResult.Ok(path, scriptName, declarations.Count);
    }

    /// <summary>
    /// Calls a script function by name.
    /// </summary>
    /// <param name="functionName">Name as declared in the <c>.pwn</c> file.</param>
    /// <param name="args">Arguments for the call, may be <c>null</c> for a parameterless call.</param>
    /// <param name="result">The return value, <c>null</c> when the call failed.</param>
    /// <param name="error">The failure, <c>null</c> when the call succeeded.</param>
    /// <returns><c>true</c> when the function ran to completion.</returns>
    /// <remarks>
    /// A failing script never throws out of this method: a broken game mode must not take the
    /// server tick down with it.
    /// </remarks>
    public bool TryInvoke(string functionName, object?[]? args, out object? result, out ScriptRuntimeException? error)
    {
        result = null;
        error = null;

        if (string.IsNullOrEmpty(functionName))
        {
            error = new ScriptRuntimeException("Es wurde kein Funktionsname angegeben.", string.Empty, 0, 0);
            return false;
        }

        ScriptFunction? function = Interpreter.FindFunction(functionName);
        if (function is null)
        {
            error = new ScriptRuntimeException(
                Interpreter.Natives.IsRegistered(functionName)
                    ? $"'{functionName}' ist eine native Funktion und kann hier nicht direkt aufgerufen werden."
                    : $"Unbekannte Funktion '{functionName}'.",
                string.Empty,
                0,
                0);
            return false;
        }

        return TryInvoke(function, args, out result, out error);
    }

    /// <summary>
    /// Calls a script function that has already been resolved.
    /// </summary>
    /// <param name="function">The function to call.</param>
    /// <param name="args">Arguments for the call, may be <c>null</c> for a parameterless call.</param>
    /// <param name="result">The return value, <c>null</c> when the call failed.</param>
    /// <param name="error">The failure, <c>null</c> when the call succeeded.</param>
    /// <returns><c>true</c> when the function ran to completion.</returns>
    public bool TryInvoke(ScriptFunction function, object?[]? args, out object? result, out ScriptRuntimeException? error)
    {
        ArgumentNullException.ThrowIfNull(function);

        result = null;
        error = null;

        BeginExecution(function.ScriptName);
        try
        {
            result = Interpreter.Call(function, args ?? []);
            return true;
        }
        catch (ScriptRuntimeException ex)
        {
            error = ex;
            return false;
        }
        catch (Exception ex)
        {
            // Belt and braces: a CLR level failure inside a native must not escape into the tick.
            error = new ScriptRuntimeException(
                $"Unerwarteter Fehler beim Aufruf von '{function.Name}': {ex.Message}",
                function.ScriptName,
                function.Declaration.Line,
                function.Declaration.Column);
            return false;
        }
        finally
        {
            EndExecution(function.ScriptName);
        }
    }

    /// <summary>
    /// Checks whether a script currently has at least one function on the call stack.
    /// </summary>
    /// <param name="scriptName">File name of the script, compared case insensitive.</param>
    /// <returns><c>true</c> while the script is executing.</returns>
    public bool IsExecuting(string scriptName)
    {
        if (string.IsNullOrEmpty(scriptName))
        {
            return false;
        }

        return _executingCounts.ContainsKey(scriptName);
    }

    private void BeginExecution(string scriptName)
    {
        _executingCounts.TryGetValue(scriptName, out int depth);
        _executingCounts[scriptName] = depth + 1;
    }

    private void EndExecution(string scriptName)
    {
        if (!_executingCounts.TryGetValue(scriptName, out int depth))
        {
            return;
        }

        if (depth <= 1)
        {
            _executingCounts.Remove(scriptName);
        }
        else
        {
            _executingCounts[scriptName] = depth - 1;
        }
    }
}
