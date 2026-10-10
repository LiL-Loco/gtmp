using GTMP.Scripting.Language;

namespace GTMP.Scripting.Tests;

/// <summary>Covers tokenisation, positions and the error reporting of the lexer.</summary>
public sealed class LexerTests
{
    [Fact]
    public void Lexer_ReportsLineAndColumnForEveryToken()
    {
        List<Token> tokens = new Lexer("new x\n    = 42;", "pos.pwn").Tokenize();

        Assert.Equal(TokenType.New, tokens[0].Type);
        Assert.Equal(1, tokens[0].Line);
        Assert.Equal(1, tokens[0].Column);

        Assert.Equal(TokenType.Identifier, tokens[1].Type);
        Assert.Equal("x", tokens[1].Text);
        Assert.Equal(1, tokens[1].Line);
        Assert.Equal(5, tokens[1].Column);

        Assert.Equal(TokenType.Assign, tokens[2].Type);
        Assert.Equal(2, tokens[2].Line);
        Assert.Equal(5, tokens[2].Column);

        Assert.Equal(42d, tokens[3].NumberValue);
        Assert.Equal(TokenType.EndOfFile, tokens[^1].Type);
    }

    [Fact]
    public void Lexer_SkipsWhitespaceAndLineComments()
    {
        List<Token> tokens = new Lexer("// leading comment\nx /* no block comments */ = 1;", "c.pwn").Tokenize();

        // The '/* ... */' text is not a comment in this subset, so it has to produce a syntax error
        // later on; here we only assert that the line comment and the blank line are gone.
        Assert.Equal(TokenType.Identifier, tokens[0].Type);
        Assert.Equal("x", tokens[0].Text);
        Assert.Equal(2, tokens[0].Line);
    }

    [Fact]
    public void Lexer_SupportsBothStringQuotesAndEscapes()
    {
        List<Token> tokens = new Lexer("\"a\\nb\" 'c\\'d'", "s.pwn").Tokenize();

        Assert.Equal(TokenType.String, tokens[0].Type);
        Assert.Equal("a\nb", tokens[0].StringValue);

        Assert.Equal(TokenType.String, tokens[1].Type);
        Assert.Equal("c'd", tokens[1].StringValue);
    }

    [Fact]
    public void Lexer_ReadsFloatLiterals()
    {
        List<Token> tokens = new Lexer("1.5 2 3e2", "f.pwn").Tokenize();

        Assert.Equal(1.5d, tokens[0].NumberValue);
        Assert.Equal(2d, tokens[1].NumberValue);
        Assert.Equal(300d, tokens[2].NumberValue);
    }

    [Fact]
    public void Lexer_ThrowsOnUnterminatedString()
    {
        ScriptSyntaxException error = Assert.Throws<ScriptSyntaxException>(
            () => new Lexer("new x = \"abc\n", "bad.pwn").Tokenize());

        Assert.Contains("Zeichenkette wird nicht geschlossen", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, error.Line);
    }

    [Fact]
    public void Lexer_ThrowsOnUnknownCharacter()
    {
        ScriptSyntaxException error = Assert.Throws<ScriptSyntaxException>(
            () => new Lexer("new x = 5 # 7;", "bad.pwn").Tokenize());

        Assert.Contains("Unbekanntes Zeichen", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, error.Line);
        Assert.Equal(11, error.Column);
    }

    [Fact]
    public void Lexer_DistinguishesCompoundAssignmentOperators()
    {
        List<Token> tokens = new Lexer("+= -= *= /= == != <= >= && ||", "op.pwn").Tokenize();

        TokenType[] expected =
        {
            TokenType.PlusAssign, TokenType.MinusAssign, TokenType.StarAssign, TokenType.SlashAssign,
            TokenType.Equal, TokenType.NotEqual, TokenType.LessOrEqual, TokenType.GreaterOrEqual,
            TokenType.And, TokenType.Or,
        };

        Assert.Equal(expected, tokens.Take(expected.Length).Select(token => token.Type));
    }
}
