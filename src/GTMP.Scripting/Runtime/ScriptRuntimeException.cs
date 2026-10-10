namespace GTMP.Scripting.Runtime;

/// <summary>
/// Raised when a syntactically valid script fails while it is being executed.
/// </summary>
/// <remarks>
/// The exception always carries the <see cref="ScriptName"/> of the script that failed plus the
/// line and column of the offending node. A game mode is edited by a server admin, not by the
/// engine, so the message has to point at a file and a position instead of surfacing a CLR stack.
/// </remarks>
public sealed class ScriptRuntimeException : Exception
{
    /// <summary>Name of the script file the failure originated from.</summary>
    public string ScriptName { get; }

    /// <summary>One based line of the failing node, or 0 when position independent.</summary>
    public int Line { get; }

    /// <summary>One based column of the failing node, or 0 when position independent.</summary>
    public int Column { get; }

    /// <summary>
    /// Creates a runtime error for a concrete source position.
    /// </summary>
    /// <param name="message">Readable description of what went wrong at runtime.</param>
    /// <param name="scriptName">Name of the script file the failure originated from.</param>
    /// <param name="line">One based line number, 0 when position independent.</param>
    /// <param name="column">One based column number, 0 when position independent.</param>
    public ScriptRuntimeException(string message, string scriptName, int line, int column)
        : base(Format(message, scriptName, line, column))
    {
        ScriptName = scriptName;
        Line = line;
        Column = column;
    }

    private static string Format(string message, string scriptName, int line, int column)
    {
        string location = string.IsNullOrEmpty(scriptName) ? "<unbekannt>" : scriptName;
        if (line <= 0) return $"{location}: {message}";
        return $"{location} (Zeile {line}, Spalte {column}): {message}";
    }
}
