using System.Globalization;
using System.Text;

namespace GTMP.Scripting.Language;

/// <summary>
/// Turns <c>.pwn</c> source text into a token list.
/// </summary>
/// <remarks>
/// Hand written and single pass. A separate lexer stage keeps the parser free of concerns like
/// escape handling and column tracking, and lets the hosting layer report a precise error for a
/// broken file before it ever allocates an AST.
/// <para>
/// The subset deliberately supports only <c>//</c> line comments; the Pawn preprocessor
/// (<c>#include</c>, <c>#define</c>, ...) is explicitly out of scope.
/// </para>
/// </remarks>
public sealed class Lexer
{
    private const string IdentifierStartChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_@";
    private const string IdentifierPartChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_@0123456789";
    private const string DigitChars = "0123456789";

    private readonly string _source;
    private readonly string _scriptName;
    private int _position;
    private int _line = 1;
    private int _lineStart;

    /// <summary>
    /// Creates a lexer for a source buffer.
    /// </summary>
    /// <param name="source">The complete <c>.pwn</c> source text.</param>
    /// <param name="scriptName">Name used in error messages, normally the file name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    public Lexer(string source, string scriptName = "<script>")
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _scriptName = string.IsNullOrEmpty(scriptName) ? "<script>" : scriptName;
    }

    /// <summary>
    /// Scans the whole source and returns the tokens, always terminated by an
    /// <see cref="TokenType.EndOfFile"/> token.
    /// </summary>
    /// <returns>The token list in source order.</returns>
    /// <exception cref="ScriptSyntaxException">The source contains an unknown character, an unterminated string or a malformed number.</exception>
    public List<Token> Tokenize()
    {
        List<Token> tokens = new(64);
        while (true)
        {
            SkipTrivia();
            if (_position >= _source.Length)
            {
                tokens.Add(Token.EndOfFile(CurrentLine, CurrentColumn));
                return tokens;
            }

            char current = _source[_position];
            if (char.IsDigit(current) || (current == '.' && _position + 1 < _source.Length && char.IsDigit(_source[_position + 1])))
            {
                tokens.Add(ReadNumber());
                continue;
            }

            if (IdentifierStartChars.IndexOf(current) >= 0)
            {
                tokens.Add(ReadIdentifierOrKeyword());
                continue;
            }

            if (current == '"' || current == '\'')
            {
                tokens.Add(ReadString());
                continue;
            }

            Token? punctuation = ReadPunctuation();
            if (punctuation is not null)
            {
                tokens.Add(punctuation);
                continue;
            }

            throw Error($"Unbekanntes Zeichen '{current}'.");
        }
    }

    private int CurrentLine => _line;

    private int CurrentColumn => _position - _lineStart + 1;

    private ScriptSyntaxException Error(string message)
        => new($"{_scriptName}: {message}", CurrentLine, CurrentColumn);

    private ScriptSyntaxException ErrorAt(string message, int line, int column)
        => new($"{_scriptName}: {message}", line, column);

    private void SkipTrivia()
    {
        while (_position < _source.Length)
        {
            char current = _source[_position];

            if (current == '\n')
            {
                _position++;
                _line++;
                _lineStart = _position;
                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                _position++;
                continue;
            }

            // CRLF: consume the '\r' here, the '\n' advances the line counter.
            if (current == '\r')
            {
                _position++;
                continue;
            }

            if (current == '/' && _position + 1 < _source.Length && _source[_position + 1] == '/')
            {
                while (_position < _source.Length && _source[_position] != '\n')
                {
                    _position++;
                }

                continue;
            }

            return;
        }
    }

    private Token ReadNumber()
    {
        int startLine = CurrentLine;
        int startColumn = CurrentColumn;
        int start = _position;
        bool isFloat = false;

        while (_position < _source.Length && DigitChars.IndexOf(_source[_position]) >= 0)
        {
            _position++;
        }

        if (_position < _source.Length && _source[_position] == '.')
        {
            // Only consume the dot when a digit follows, otherwise it is a member access.
            if (_position + 1 < _source.Length && DigitChars.IndexOf(_source[_position + 1]) >= 0)
            {
                isFloat = true;
                _position++;
                while (_position < _source.Length && DigitChars.IndexOf(_source[_position]) >= 0)
                {
                    _position++;
                }
            }
        }

        if (_position < _source.Length && (_source[_position] == 'e' || _source[_position] == 'E'))
        {
            int exponentPosition = _position + 1;
            if (exponentPosition < _source.Length && (_source[exponentPosition] == '+' || _source[exponentPosition] == '-'))
            {
                exponentPosition++;
            }

            if (exponentPosition < _source.Length && DigitChars.IndexOf(_source[exponentPosition]) >= 0)
            {
                isFloat = true;
                _position = exponentPosition;
                while (_position < _source.Length && DigitChars.IndexOf(_source[_position]) >= 0)
                {
                    _position++;
                }
            }
        }

        string text = _source.Substring(start, _position - start);
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            throw ErrorAt($"Ungültige Zahl '{text}'.", startLine, startColumn);
        }

        if (!isFloat)
        {
            // Guard against silently turning a huge integer literal into an infinity.
            if (double.IsInfinity(value))
            {
                throw ErrorAt($"Zahl '{text}' ist zu groß.", startLine, startColumn);
            }
        }

        return Token.Number(value, text, startLine, startColumn);
    }

    private Token ReadIdentifierOrKeyword()
    {
        int startLine = CurrentLine;
        int startColumn = CurrentColumn;
        int start = _position;

        while (_position < _source.Length && IdentifierPartChars.IndexOf(_source[_position]) >= 0)
        {
            _position++;
        }

        string text = _source.Substring(start, _position - start);
        TokenType type = text switch
        {
            "new" => TokenType.New,
            "var" => TokenType.Var,
            "if" => TokenType.If,
            "else" => TokenType.Else,
            "while" => TokenType.While,
            "for" => TokenType.For,
            "foreach" => TokenType.Foreach,
            "return" => TokenType.Return,
            "break" => TokenType.Break,
            "continue" => TokenType.Continue,
            "true" => TokenType.True,
            "false" => TokenType.False,
            "null" => TokenType.Null,
            _ => TokenType.Identifier,
        };

        return type == TokenType.Identifier
            ? Token.Identifier(text, startLine, startColumn)
            : Token.Simple(type, text, startLine, startColumn);
    }

    private Token ReadString()
    {
        int startLine = CurrentLine;
        int startColumn = CurrentColumn;
        int rawStart = _position;
        char quote = _source[_position];
        _position++;

        StringBuilder builder = new();
        while (true)
        {
            if (_position >= _source.Length)
            {
                throw ErrorAt("Zeichenkette wird nicht geschlossen.", startLine, startColumn);
            }

            char current = _source[_position];

            if (current == '\n')
            {
                // A raw newline inside a string is always a bug in the script.
                throw ErrorAt("Zeichenkette wird nicht geschlossen.", startLine, startColumn);
            }

            if (current == quote)
            {
                _position++;
                break;
            }

            if (current == '\\')
            {
                _position++;
                builder.Append(ReadEscape(startLine, startColumn));
                continue;
            }

            builder.Append(current);
            _position++;
        }

        return Token.String(builder.ToString(), _source.Substring(rawStart, _position - rawStart), startLine, startColumn);
    }

    private string ReadEscape(int startLine, int startColumn)
    {
        if (_position >= _source.Length)
        {
            throw ErrorAt("Unvollständige Escape-Sequenz.", startLine, startColumn);
        }

        char current = _source[_position];
        _position++;
        switch (current)
        {
            case 'n': return "\n";
            case 'r': return "\r";
            case 't': return "\t";
            case '0': return "\0";
            case '\\': return "\\";
            case '"': return "\"";
            case '\'': return "'";
            default:
                throw ErrorAt($"Unbekannte Escape-Sequenz '\\{current}'.", startLine, startColumn);
        }
    }

    private Token? ReadPunctuation()
    {
        int startLine = CurrentLine;
        int startColumn = CurrentColumn;
        char current = _source[_position];

        // Two character operators have to be checked before their single character prefixes.
        if (_position + 1 < _source.Length)
        {
            char next = _source[_position + 1];
            (TokenType type, string text)? pair = (current, next) switch
            {
                ('=', '=') => (TokenType.Equal, "=="),
                ('!', '=') => (TokenType.NotEqual, "!="),
                ('<', '=') => (TokenType.LessOrEqual, "<="),
                ('>', '=') => (TokenType.GreaterOrEqual, ">="),
                ('&', '&') => (TokenType.And, "&&"),
                ('|', '|') => (TokenType.Or, "||"),
                ('+', '=') => (TokenType.PlusAssign, "+="),
                ('-', '=') => (TokenType.MinusAssign, "-="),
                ('*', '=') => (TokenType.StarAssign, "*="),
                ('/', '=') => (TokenType.SlashAssign, "/="),
                _ => null,
            };

            if (pair is not null)
            {
                _position += 2;
                return Token.Simple(pair.Value.type, pair.Value.text, startLine, startColumn);
            }
        }

        TokenType single = current switch
        {
            '(' => TokenType.LeftParen,
            ')' => TokenType.RightParen,
            '[' => TokenType.LeftBracket,
            ']' => TokenType.RightBracket,
            '{' => TokenType.LeftBrace,
            '}' => TokenType.RightBrace,
            ',' => TokenType.Comma,
            ';' => TokenType.Semicolon,
            '.' => TokenType.Dot,
            '+' => TokenType.Plus,
            '-' => TokenType.Minus,
            '*' => TokenType.Star,
            '/' => TokenType.Slash,
            '%' => TokenType.Percent,
            '=' => TokenType.Assign,
            '<' => TokenType.Less,
            '>' => TokenType.Greater,
            '!' => TokenType.Bang,
            _ => TokenType.EndOfFile,
        };

        if (single == TokenType.EndOfFile)
        {
            return null;
        }

        _position++;
        return Token.Simple(single, current.ToString(), startLine, startColumn);
    }
}
