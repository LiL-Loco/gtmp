namespace GTMP.Scripting.Hosting;

/// <summary>
/// Outcome of a <see cref="ScriptEngine.Load"/> call.
/// </summary>
/// <remarks>
/// Loading a script can fail for three very different reasons - a missing file, a syntax error in
/// the file, or a duplicate function name. All of them are reported here instead of by throwing,
/// because a game mode reload is a routine server operation and must not blow up the tick that
/// triggered it.
/// </remarks>
public sealed class ScriptLoadResult
{
    private ScriptLoadResult(bool success, string path, string scriptName, int functionCount, string? error, int errorLine, int errorColumn)
    {
        Success = success;
        Path = path;
        ScriptName = scriptName;
        FunctionCount = functionCount;
        Error = error;
        ErrorLine = errorLine;
        ErrorColumn = errorColumn;
    }

    /// <summary>Whether the script was loaded and its functions are callable.</summary>
    public bool Success { get; }

    /// <summary>The path that was requested.</summary>
    public string Path { get; }

    /// <summary>The file name used in diagnostics, without directory.</summary>
    public string ScriptName { get; }

    /// <summary>Number of functions the script contributed, 0 on failure.</summary>
    public int FunctionCount { get; }

    /// <summary>Human readable failure description, <c>null</c> on success.</summary>
    public string? Error { get; }

    /// <summary>One based line of a syntax error, 0 otherwise.</summary>
    public int ErrorLine { get; }

    /// <summary>One based column of a syntax error, 0 otherwise.</summary>
    public int ErrorColumn { get; }

    /// <summary>
    /// Creates a successful load result.
    /// </summary>
    /// <param name="path">The loaded path.</param>
    /// <param name="scriptName">File name used in diagnostics.</param>
    /// <param name="functionCount">Number of registered functions.</param>
    /// <returns>The created result.</returns>
    public static ScriptLoadResult Ok(string path, string scriptName, int functionCount)
        => new(true, path, scriptName, functionCount, null, 0, 0);

    /// <summary>
    /// Creates a failed load result.
    /// </summary>
    /// <param name="path">The requested path.</param>
    /// <param name="error">Failure description.</param>
    /// <param name="line">One based line of the error, 0 when position independent.</param>
    /// <param name="column">One based column of the error, 0 when position independent.</param>
    /// <returns>The created result.</returns>
    public static ScriptLoadResult Fail(string path, string error, int line = 0, int column = 0)
        => new(false, path, System.IO.Path.GetFileName(path), 0, error, line, column);

    /// <inheritdoc />
    public override string ToString()
        => Success ? $"OK {ScriptName} ({FunctionCount} Funktion(en))" : $"FEHLER {ScriptName}: {Error}";
}
