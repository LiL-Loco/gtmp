namespace GTMP.Scripting.Language;

/// <summary>
/// A single lexical token together with the source position it came from.
/// </summary>
/// <remarks>
/// Positions are kept on every token rather than only on the ones the parser happens to reject,
/// because a runtime error in a later release still has to point the script author at a column.
/// </remarks>
public sealed class Token
{
    private Token(TokenType type, string text, int line, int column, double numberValue, string stringValue)
    {
        Type = type;
        Text = text;
        Line = line;
        Column = column;
        NumberValue = numberValue;
        StringValue = stringValue;
    }

    /// <summary>Lexical category of this token.</summary>
    public TokenType Type { get; }

    /// <summary>Raw source text, used for identifiers, keywords, operators and error messages.</summary>
    public string Text { get; }

    /// <summary>One based line the token starts on.</summary>
    public int Line { get; }

    /// <summary>One based column the token starts on.</summary>
    public int Column { get; }

    /// <summary>Numeric payload, only meaningful when <see cref="Type"/> is <see cref="TokenType.Number"/>.</summary>
    public double NumberValue { get; }

    /// <summary>Unescaped payload, only meaningful when <see cref="Type"/> is <see cref="TokenType.String"/>.</summary>
    public string StringValue { get; }

    /// <summary>Creates an identifier token.</summary>
    /// <param name="text">The identifier as written in the source.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    /// <returns>The created token.</returns>
    public static Token Identifier(string text, int line, int column)
        => new(TokenType.Identifier, text, line, column, 0d, string.Empty);

    /// <summary>Creates a numeric literal token.</summary>
    /// <param name="value">Parsed numeric value.</param>
    /// <param name="text">The literal as written in the source.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    /// <returns>The created token.</returns>
    public static Token Number(double value, string text, int line, int column)
        => new(TokenType.Number, text, line, column, value, string.Empty);

    /// <summary>Creates a string literal token.</summary>
    /// <param name="value">Unescaped string value.</param>
    /// <param name="text">The literal including its quotes, as written in the source.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    /// <returns>The created token.</returns>
    public static Token String(string value, string text, int line, int column)
        => new(TokenType.String, text, line, column, 0d, value);

    /// <summary>Creates a token for a keyword, operator or punctuation mark.</summary>
    /// <param name="type">Category of the token.</param>
    /// <param name="text">Source text of the token.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    /// <returns>The created token.</returns>
    public static Token Simple(TokenType type, string text, int line, int column)
        => new(type, text, line, column, 0d, string.Empty);

    /// <summary>Creates the end of file sentinel.</summary>
    /// <param name="line">One based line number the file ends on.</param>
    /// <param name="column">One based column number the file ends on.</param>
    /// <returns>The created token.</returns>
    public static Token EndOfFile(int line, int column)
        => new(TokenType.EndOfFile, "Ende der Datei", line, column, 0d, string.Empty);

    /// <inheritdoc />
    public override string ToString() => $"{Type} '{Text}' ({Line}:{Column})";
}
