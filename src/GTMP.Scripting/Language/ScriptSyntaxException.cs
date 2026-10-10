namespace GTMP.Scripting.Language;

/// <summary>
/// Raised whenever a <c>.pwn</c> source cannot be turned into tokens or an AST.
/// </summary>
/// <remarks>
/// A dedicated type is used instead of a plain <see cref="System.Exception"/> so that the
/// hosting layer can distinguish "the script author made a mistake" from "the script blew up
/// at runtime" and report the two differently - a syntax error must never be logged as a
/// server fault.
/// </remarks>
public sealed class ScriptSyntaxException : Exception
{
    /// <summary>One based line the error was detected on, or 0 when no source position applies.</summary>
    public int Line { get; }

    /// <summary>One based column the error was detected on, or 0 when no source position applies.</summary>
    public int Column { get; }

    /// <summary>
    /// Creates a syntax error for a concrete source position.
    /// </summary>
    /// <param name="message">Readable description of what is wrong with the source.</param>
    /// <param name="line">One based line number, 0 when position independent.</param>
    /// <param name="column">One based column number, 0 when position independent.</param>
    public ScriptSyntaxException(string message, int line, int column)
        : base(Format(message, line, column))
    {
        Line = line;
        Column = column;
    }

    /// <summary>
    /// Creates a syntax error that wraps an underlying failure, used when reading or
    /// lexing a source file fails for a reason that is still the author's problem.
    /// </summary>
    /// <param name="message">Readable description of what is wrong with the source.</param>
    /// <param name="line">One based line number, 0 when position independent.</param>
    /// <param name="column">One based column number, 0 when position independent.</param>
    /// <param name="innerException">The failure that triggered this syntax error.</param>
    public ScriptSyntaxException(string message, int line, int column, Exception innerException)
        : base(Format(message, line, column), innerException)
    {
        Line = line;
        Column = column;
    }

    private static string Format(string message, int line, int column)
    {
        if (line <= 0) return message;
        return $"Zeile {line}, Spalte {column}: {message}";
    }
}
