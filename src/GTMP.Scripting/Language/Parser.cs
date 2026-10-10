namespace GTMP.Scripting.Language;

/// <summary>
/// Recursive descent parser for the supported Pawn subset.
/// </summary>
/// <remarks>
/// One token of lookahead is enough for this grammar, because none of the productions is
/// ambiguous beyond what the token type alone decides.
/// <para>
/// Precedence, from tightest to loosest:
/// unary (<c>-</c>, <c>!</c>) - <c>* / %</c> - <c>+ -</c> - comparisons
/// (<c>&lt; &lt;= &gt; &gt;=</c>) - <c>== !=</c> - <c>&amp;&amp;</c> - <c>||</c> - assignment.
/// </para>
/// </remarks>
public sealed class Parser
{
    private readonly List<Token> _tokens;
    private readonly string _scriptName;
    private int _index;

    /// <summary>
    /// Creates a parser over a token list that has to be terminated by
    /// <see cref="TokenType.EndOfFile"/>.
    /// </summary>
    /// <param name="tokens">The tokens produced by <see cref="Lexer"/>.</param>
    /// <param name="scriptName">Name used in error messages, normally the file name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tokens"/> is null.</exception>
    /// <exception cref="ScriptSyntaxException">The token list is empty or not terminated by end of file.</exception>
    public Parser(IReadOnlyList<Token> tokens, string scriptName = "<script>")
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0 || tokens[tokens.Count - 1].Type != TokenType.EndOfFile)
        {
            throw new ScriptSyntaxException(
                $"{scriptName}: Die Token-Liste muss mit einem Ende-der-Datei-Token abgeschlossen werden.",
                0,
                0);
        }

        _tokens = new List<Token>(tokens);
        _scriptName = string.IsNullOrEmpty(scriptName) ? "<script>" : scriptName;
    }

    private Token Current => _tokens[_index];

    private Token Previous => _tokens[_index - 1];

    /// <summary>
    /// Parses a complete script: global variable declarations and function declarations,
    /// in any order.
    /// </summary>
    /// <returns>The global declarations and the functions, both in source order.</returns>
    /// <exception cref="ScriptSyntaxException">The source does not match the supported grammar.</exception>
    public ParsedProgram ParseTopLevel()
    {
        List<VariableDeclaration> globals = new(8);
        List<FunctionDeclaration> functions = new(8);

        while (Current.Type != TokenType.EndOfFile)
        {
            if (Current.Type == TokenType.New)
                globals.Add((VariableDeclaration)ParseVariableDeclaration());
            else
                functions.Add(ParseFunctionDeclaration());
        }

        return new ParsedProgram(globals, functions);
    }

    /// <summary>
    /// Parses a complete script and returns only its functions.
    /// </summary>
    /// <param name="returns">The function declarations in source order.</param>
    /// <exception cref="ScriptSyntaxException">The source does not match the supported grammar.</exception>
    public IReadOnlyList<FunctionDeclaration> ParseProgram()
        => ParseTopLevel().Functions;

    private FunctionDeclaration ParseFunctionDeclaration()
    {
        Token name = Expect(TokenType.Identifier, "Funktionsname erwartet");

        if (!Match(TokenType.LeftParen))
        {
            throw ErrorAt(Previous, "'(' erwartet, um die Parameterliste zu öffnen.");
        }

        List<string> parameters = new(4);
        if (Current.Type != TokenType.RightParen)
        {
            do
            {
                Token parameter = Expect(TokenType.Identifier, "Parametername erwartet");
                if (parameters.Contains(parameter.Text, StringComparer.Ordinal))
                {
                    throw ErrorAt(parameter, $"Der Parameter '{parameter.Text}' ist doppelt deklariert.");
                }

                parameters.Add(parameter.Text);
            }
            while (Match(TokenType.Comma));

            if (Current.Type != TokenType.RightParen)
            {
                throw ErrorAt(Current, "')' erwartet, um die Parameterliste zu schließen.");
            }
        }

        _index++;

        BlockStatement body = ParseBlock();

        // A trailing semicolon after the closing brace is tolerated, Pawn authors add it out of habit.
        Match(TokenType.Semicolon);

        return new FunctionDeclaration(name.Text, parameters, body, name.Line, name.Column);
    }

    private BlockStatement ParseBlock()
    {
        Token open = Expect(TokenType.LeftBrace, "'{' erwartet, um einen Block zu öffnen");
        List<Statement> statements = new(8);

        while (Current.Type != TokenType.RightBrace)
        {
            if (Current.Type == TokenType.EndOfFile)
            {
                throw ErrorAt(Current, "'}' erwartet, aber die Datei endet vorher.");
            }

            statements.Add(ParseStatement());
        }

        _index++;
        return new BlockStatement(statements, open.Line, open.Column);
    }

    private Statement ParseStatement()
    {
        Token current = Current;

        switch (current.Type)
        {
            case TokenType.LeftBrace:
                return ParseBlock();

            case TokenType.New:
            case TokenType.Var:
                return ParseVariableDeclaration();

            case TokenType.If:
                return ParseIfStatement();

            case TokenType.While:
                return ParseWhileStatement();

            case TokenType.For:
                return ParseForStatement();

            case TokenType.Foreach:
                return ParseForEachStatement();

            case TokenType.Return:
                return ParseReturnStatement();

            case TokenType.Break:
                _index++;
                Expect(TokenType.Semicolon, "';' nach 'break' erwartet");
                return new BreakStatement(current.Line, current.Column);

            case TokenType.Continue:
                _index++;
                Expect(TokenType.Semicolon, "';' nach 'continue' erwartet");
                return new ContinueStatement(current.Line, current.Column);

            default:
                return ParseExpressionStatement();
        }
    }

    private Statement ParseVariableDeclaration()
    {
        Token keyword = _tokens[_index++]; // consume 'new' or 'var'
        Token name = Expect(TokenType.Identifier, "Variablenname nach 'new' erwartet");
        Expect(TokenType.Assign, "'=' erwartet, jeder 'new'-Deklaration braucht einen Startwert");

        Expression initializer = ParseExpression();
        Expect(TokenType.Semicolon, "';' am Ende der Deklaration erwartet");

        return new VariableDeclaration(name.Text, initializer, keyword.Line, keyword.Column);
    }

    private Statement ParseExpressionStatement()
    {
        Token start = Current;
        Expression expression = ParseExpression();
        Expect(TokenType.Semicolon, "';' am Ende des Ausdrucks erwartet");
        return new ExpressionStatement(expression, start.Line, start.Column);
    }

    private Statement ParseIfStatement()
    {
        Token keyword = _tokens[_index++]; // consume 'if'
        Expect(TokenType.LeftParen, "'(' nach 'if' erwartet");
        Expression condition = ParseExpression();
        Expect(TokenType.RightParen, "')' nach der if-Bedingung erwartet");

        Statement thenBranch = ParseStatement();

        Statement? elseBranch = null;
        if (Match(TokenType.Else))
        {
            // 'else if' chains are just a nested if sitting in the else slot.
            elseBranch = Current.Type == TokenType.If ? ParseIfStatement() : ParseStatement();
        }

        return new IfStatement(condition, thenBranch, elseBranch, keyword.Line, keyword.Column);
    }

    private Statement ParseWhileStatement()
    {
        Token keyword = _tokens[_index++]; // consume 'while'
        Expect(TokenType.LeftParen, "'(' nach 'while' erwartet");
        Expression condition = ParseExpression();
        Expect(TokenType.RightParen, "')' nach der while-Bedingung erwartet");

        Statement body = ParseStatement();
        return new WhileStatement(condition, body, keyword.Line, keyword.Column);
    }

    private Statement ParseForStatement()
    {
        Token keyword = _tokens[_index++]; // consume 'for'
        Expect(TokenType.LeftParen, "'(' nach 'for' erwartet");

        Statement? initializer = null;
        if (Current.Type == TokenType.New || Current.Type == TokenType.Var)
        {
            initializer = ParseVariableDeclaration();
        }
        else if (Current.Type != TokenType.Semicolon)
        {
            initializer = ParseExpressionStatement();
        }
        else
        {
            _index++; // empty initialiser
        }

        Expression? condition = Current.Type == TokenType.Semicolon ? null : ParseExpression();
        Expect(TokenType.Semicolon, "';' nach der for-Bedingung erwartet");

        Statement? update = null;
        if (Current.Type != TokenType.RightParen)
        {
            Token updateStart = Current;
            update = new ExpressionStatement(ParseExpression(), updateStart.Line, updateStart.Column);
        }

        Expect(TokenType.RightParen, "')' nach dem for-Update erwartet");
        Statement body = ParseStatement();

        return new ForStatement(initializer, condition, update, body, keyword.Line, keyword.Column);
    }

    private Statement ParseForEachStatement()
    {
        Token keyword = _tokens[_index++]; // consume 'foreach'
        Expect(TokenType.LeftParen, "'(' nach 'foreach' erwartet");
        Token variable = Expect(TokenType.Identifier, "Name der foreach-Variable erwartet");

        if (Current.Type != TokenType.Identifier || Current.Text != "in")
        {
            throw ErrorAt(Current, "'in' in der foreach-Kopfzeile erwartet");
        }

        _index++;

        Expression collection = ParseExpression();
        Expect(TokenType.RightParen, "')' am Ende der foreach-Kopfzeile erwartet");

        Statement body = ParseStatement();
        return new ForEachStatement(variable.Text, collection, body, keyword.Line, keyword.Column);
    }

    private Statement ParseReturnStatement()
    {
        Token keyword = _tokens[_index++]; // consume 'return'

        Expression? value = null;
        if (Current.Type != TokenType.Semicolon)
        {
            if (Current.Type == TokenType.EndOfFile || Current.Type == TokenType.RightBrace)
            {
                throw ErrorAt(Current, "Nach 'return' wird ein Wert oder ';' erwartet.");
            }

            value = ParseExpression();
        }

        Expect(TokenType.Semicolon, "';' nach 'return' erwartet");
        return new ReturnStatement(value, keyword.Line, keyword.Column);
    }

    private Expression ParseExpression() => ParseAssignment();

    private Expression ParseAssignment()
    {
        Expression left = ParseLogicalOr();

        TokenType op = Current.Type switch
        {
            TokenType.Assign => TokenType.Assign,
            TokenType.PlusAssign => TokenType.PlusAssign,
            TokenType.MinusAssign => TokenType.MinusAssign,
            TokenType.StarAssign => TokenType.StarAssign,
            TokenType.SlashAssign => TokenType.SlashAssign,
            _ => TokenType.EndOfFile,
        };

        if (op == TokenType.EndOfFile)
        {
            return left;
        }

        Token token = _tokens[_index++];
        Expression value = ParseAssignment(); // right associative, so 'a = b = 1' works

        try
        {
            return new AssignmentExpression(left, op, value, token.Line, token.Column);
        }
        catch (ArgumentException ex)
        {
            throw ErrorAt(token, ex.Message);
        }
    }

    private Expression ParseLogicalOr()
    {
        Expression left = ParseLogicalAnd();
        while (Match(TokenType.Or))
        {
            Token op = Previous;
            left = new BinaryExpression(TokenType.Or, left, ParseLogicalAnd(), op.Line, op.Column);
        }

        return left;
    }

    private Expression ParseLogicalAnd()
    {
        Expression left = ParseEquality();
        while (Match(TokenType.And))
        {
            Token op = Previous;
            left = new BinaryExpression(TokenType.And, left, ParseEquality(), op.Line, op.Column);
        }

        return left;
    }

    private Expression ParseEquality()
    {
        Expression left = ParseComparison();
        while (Current.Type is TokenType.Equal or TokenType.NotEqual)
        {
            Token op = _tokens[_index++];
            left = new BinaryExpression(op.Type, left, ParseComparison(), op.Line, op.Column);
        }

        return left;
    }

    private Expression ParseComparison()
    {
        Expression left = ParseAdditive();
        while (Current.Type is TokenType.Less or TokenType.LessOrEqual or TokenType.Greater or TokenType.GreaterOrEqual)
        {
            Token op = _tokens[_index++];
            left = new BinaryExpression(op.Type, left, ParseAdditive(), op.Line, op.Column);
        }

        return left;
    }

    private Expression ParseAdditive()
    {
        Expression left = ParseMultiplicative();
        while (Current.Type is TokenType.Plus or TokenType.Minus)
        {
            Token op = _tokens[_index++];
            left = new BinaryExpression(op.Type, left, ParseMultiplicative(), op.Line, op.Column);
        }

        return left;
    }

    private Expression ParseMultiplicative()
    {
        Expression left = ParseUnary();
        while (Current.Type is TokenType.Star or TokenType.Slash or TokenType.Percent)
        {
            Token op = _tokens[_index++];
            left = new BinaryExpression(op.Type, left, ParseUnary(), op.Line, op.Column);
        }

        return left;
    }

    private Expression ParseUnary()
    {
        if (Current.Type is TokenType.Minus or TokenType.Bang)
        {
            Token op = _tokens[_index++];
            return new UnaryExpression(op.Type, ParseUnary(), op.Line, op.Column);
        }

        return ParsePostfix();
    }

    private Expression ParsePostfix()
    {
        Expression expression = ParsePrimary();

        while (Current.Type == TokenType.LeftBracket)
        {
            Token open = _tokens[_index++];
            Expression index = ParseExpression();
            Expect(TokenType.RightBracket, "']' nach dem Array-Index erwartet");
            expression = new IndexExpression(expression, index, open.Line, open.Column);
        }

        return expression;
    }

    private Expression ParsePrimary()
    {
        Token token = Current;

        switch (token.Type)
        {
            case TokenType.Number:
                _index++;
                return new NumberLiteral(token.NumberValue, token.Line, token.Column);

            case TokenType.String:
                _index++;
                return new StringLiteral(token.StringValue, token.Line, token.Column);

            case TokenType.True:
                _index++;
                return new BoolLiteral(true, token.Line, token.Column);

            case TokenType.False:
                _index++;
                return new BoolLiteral(false, token.Line, token.Column);

            case TokenType.Null:
                _index++;
                return new NullLiteral(token.Line, token.Column);

            case TokenType.LeftParen:
            {
                _index++;
                Expression inner = ParseExpression();
                Expect(TokenType.RightParen, "')' erwartet, um den Ausdruck zu schließen");
                return inner;
            }

            case TokenType.LeftBracket:
                return ParseArrayLiteral();

            case TokenType.Identifier:
                return ParseIdentifierExpression();

            default:
                throw ErrorAt(token, $"Unerwartetes Token '{token.Text}' im Ausdruck.");
        }
    }

    private Expression ParseArrayLiteral()
    {
        Token open = Expect(TokenType.LeftBracket, "'[' erwartet");
        List<Expression> elements = new(4);

        if (Current.Type != TokenType.RightBracket)
        {
            do
            {
                elements.Add(ParseExpression());
            }
            while (Match(TokenType.Comma));
        }

        if (Current.Type != TokenType.RightBracket)
        {
            throw ErrorAt(Current, "']' oder ',' im Array-Literal erwartet.");
        }

        _index++;
        return new ArrayLiteralExpression(elements, open.Line, open.Column);
    }

    private Expression ParseIdentifierExpression()
    {
        Token name = _tokens[_index++];

        // Dotted names are how natives are addressed ('Player.GiveMoney'); there is no property access.
        while (Current.Type == TokenType.Dot)
        {
            _index++;
            Token part = Expect(TokenType.Identifier, "Name nach '.' erwartet");
            name = Token.Simple(TokenType.Identifier, name.Text + "." + part.Text, name.Line, name.Column);
        }

        if (Current.Type != TokenType.LeftParen)
        {
            return new VariableReference(name.Text, name.Line, name.Column);
        }

        _index++;
        List<Expression> arguments = new(4);

        if (Current.Type != TokenType.RightParen)
        {
            do
            {
                arguments.Add(ParseExpression());
            }
            while (Match(TokenType.Comma));
        }

        if (Current.Type != TokenType.RightParen)
        {
            throw ErrorAt(Current, $"')' erwartet, um die Argumente von '{name.Text}' zu schließen.");
        }

        _index++;
        return new CallExpression(name.Text, arguments, name.Line, name.Column);
    }

    private bool Match(TokenType type)
    {
        if (Current.Type != type)
        {
            return false;
        }

        _index++;
        return true;
    }

    private Token Expect(TokenType type, string message)
    {
        if (Current.Type != type)
        {
            throw ErrorAt(Current, message);
        }

        return _tokens[_index++];
    }

    private ScriptSyntaxException ErrorAt(Token token, string message)
        => new($"{_scriptName}: {message}", token.Line, token.Column);

    /// <summary>
    /// Convenience entry point that lexes and parses a source string in one go.
    /// </summary>
    /// <param name="source">The complete <c>.pwn</c> source text.</param>
    /// <param name="scriptName">Name used in error messages, normally the file name.</param>
    /// <returns>The declarations in source order.</returns>
    /// <exception cref="ScriptSyntaxException">The source cannot be lexed or parsed.</exception>
    public static IReadOnlyList<FunctionDeclaration> ParseSource(string source, string scriptName = "<script>")
    {
        Lexer lexer = new(source, scriptName);
        return new Parser(lexer.Tokenize(), scriptName).ParseProgram();
    }
}
