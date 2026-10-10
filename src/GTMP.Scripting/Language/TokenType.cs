namespace GTMP.Scripting.Language;

/// <summary>
/// The lexical categories the Pawn subset recognises.
/// </summary>
/// <remarks>
/// Keywords get their own categories instead of being tagged <see cref="Identifier"/> so the
/// parser can dispatch on the token type without a string comparison per token, which matters
/// on a 50 ms tick budget.
/// </remarks>
public enum TokenType
{
    /// <summary>End of the token stream; also used as the "not found" sentinel.</summary>
    EndOfFile = 0,

    /// <summary>A variable or function name.</summary>
    Identifier,

    /// <summary>An integer or floating point literal, see <see cref="Token.NumberValue"/>.</summary>
    Number,

    /// <summary>A quoted string literal, see <see cref="Token.StringValue"/>.</summary>
    String,

    /// <summary>Keyword <c>new</c>, introduces a variable declaration.</summary>
    New,

    /// <summary>Keyword <c>var</c>, C style alias for <see cref="New"/>.</summary>
    Var,

    /// <summary>Keyword <c>if</c>.</summary>
    If,

    /// <summary>Keyword <c>else</c>.</summary>
    Else,

    /// <summary>Keyword <c>while</c>.</summary>
    While,

    /// <summary>Keyword <c>for</c>.</summary>
    For,

    /// <summary>Keyword <c>foreach</c>.</summary>
    Foreach,

    /// <summary>Keyword <c>return</c>.</summary>
    Return,

    /// <summary>Keyword <c>break</c>.</summary>
    Break,

    /// <summary>Keyword <c>continue</c>.</summary>
    Continue,

    /// <summary>Keyword <c>true</c>.</summary>
    True,

    /// <summary>Keyword <c>false</c>.</summary>
    False,

    /// <summary>Keyword <c>null</c>.</summary>
    Null,

    /// <summary>Opening parenthesis <c>(</c>.</summary>
    LeftParen,

    /// <summary>Closing parenthesis <c>)</c>.</summary>
    RightParen,

    /// <summary>Opening bracket <c>[</c>.</summary>
    LeftBracket,

    /// <summary>Closing bracket <c>]</c>.</summary>
    RightBracket,

    /// <summary>Opening brace <c>{</c>.</summary>
    LeftBrace,

    /// <summary>Closing brace <c>}</c>.</summary>
    RightBrace,

    /// <summary>Comma <c>,</c>.</summary>
    Comma,

    /// <summary>Semicolon <c>;</c>.</summary>
    Semicolon,

    /// <summary>Dot <c>.</c>, used to build dotted native names such as <c>Player.GiveMoney</c>.</summary>
    Dot,

    /// <summary>Addition or unary plus.</summary>
    Plus,

    /// <summary>Subtraction or unary minus.</summary>
    Minus,

    /// <summary>Multiplication.</summary>
    Star,

    /// <summary>Division.</summary>
    Slash,

    /// <summary>Remainder.</summary>
    Percent,

    /// <summary>Assignment <c>=</c>.</summary>
    Assign,

    /// <summary>Equality comparison <c>==</c>.</summary>
    Equal,

    /// <summary>Inequality comparison <c>!=</c>.</summary>
    NotEqual,

    /// <summary>Less than <c>&lt;</c>.</summary>
    Less,

    /// <summary>Less than or equal <c>&lt;=</c>.</summary>
    LessOrEqual,

    /// <summary>Greater than <c>&gt;</c>.</summary>
    Greater,

    /// <summary>Greater than or equal <c>&gt;=</c>.</summary>
    GreaterOrEqual,

    /// <summary>Short circuiting logical and <c>&amp;&amp;</c>.</summary>
    And,

    /// <summary>Short circuiting logical or <c>||</c>.</summary>
    Or,

    /// <summary>Logical not <c>!</c>.</summary>
    Bang,

    /// <summary>Compound assignment <c>+=</c>.</summary>
    PlusAssign,

    /// <summary>Compound assignment <c>-=</c>.</summary>
    MinusAssign,

    /// <summary>Compound assignment <c>*=</c>.</summary>
    StarAssign,

    /// <summary>Compound assignment <c>/=</c>.</summary>
    SlashAssign,
}
