namespace GTMP.Scripting.Language;

/// <summary>
/// Base class of every AST node.
/// </summary>
/// <remarks>
/// The position is captured at construction time so that a runtime error can point at the exact
/// place in the <c>.pwn</c> file, long after parsing and lexing buffers are gone.
/// </remarks>
public abstract class Node
{
    /// <summary>Creates a node at a concrete source position.</summary>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    protected Node(int line, int column)
    {
        Line = line;
        Column = column;
    }

    /// <summary>One based line the node starts on.</summary>
    public int Line { get; }

    /// <summary>One based column the node starts on.</summary>
    public int Column { get; }
}

/// <summary>Base class of every node that produces a value.</summary>
public abstract class Expression : Node
{
    /// <summary>Creates an expression node at a concrete source position.</summary>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    protected Expression(int line, int column) : base(line, column) { }
}

/// <summary>Base class of every node that is executed for its effect.</summary>
public abstract class Statement : Node
{
    /// <summary>Creates a statement node at a concrete source position.</summary>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    protected Statement(int line, int column) : base(line, column) { }
}

/// <summary>An integer or floating point literal, always a <see cref="double"/> at runtime.</summary>
public sealed class NumberLiteral : Expression
{
    /// <summary>Creates a numeric literal node.</summary>
    /// <param name="value">The literal value.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public NumberLiteral(double value, int line, int column) : base(line, column) => Value = value;

    /// <summary>The literal value.</summary>
    public double Value { get; }
}

/// <summary>A string literal; single and double quoted strings are equivalent.</summary>
public sealed class StringLiteral : Expression
{
    /// <summary>Creates a string literal node.</summary>
    /// <param name="value">The unescaped string value.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public StringLiteral(string value, int line, int column) : base(line, column) => Value = value;

    /// <summary>The unescaped string value.</summary>
    public string Value { get; }
}

/// <summary>The literal <c>true</c> or <c>false</c>.</summary>
public sealed class BoolLiteral : Expression
{
    /// <summary>Creates a boolean literal node.</summary>
    /// <param name="value">The literal value.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public BoolLiteral(bool value, int line, int column) : base(line, column) => Value = value;

    /// <summary>The literal value.</summary>
    public bool Value { get; }
}

/// <summary>The literal <c>null</c>.</summary>
public sealed class NullLiteral : Expression
{
    /// <summary>Creates a null literal node.</summary>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public NullLiteral(int line, int column) : base(line, column) { }
}

/// <summary>
/// An array literal such as <c>[1, "two", null]</c>.
/// </summary>
/// <remarks>
/// Not part of classic Pawn, where arrays are declared with <c>new name[size]</c>. A literal is
/// used here because it fits the recursive descent parser without a second declaration form and
/// keeps the subset small.
/// </remarks>
public sealed class ArrayLiteralExpression : Expression
{
    /// <summary>Creates an array literal node.</summary>
    /// <param name="elements">The element expressions, evaluated left to right.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public ArrayLiteralExpression(IReadOnlyList<Expression> elements, int line, int column) : base(line, column)
        => Elements = elements;

    /// <summary>The element expressions.</summary>
    public IReadOnlyList<Expression> Elements { get; }
}

/// <summary>A read of a variable by name.</summary>
public sealed class VariableReference : Expression
{
    /// <summary>Creates a variable read node.</summary>
    /// <param name="name">Variable name.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public VariableReference(string name, int line, int column) : base(line, column) => Name = name;

    /// <summary>Variable name.</summary>
    public string Name { get; }
}

/// <summary>A prefix operator, currently only <c>-</c> and <c>!</c>.</summary>
public sealed class UnaryExpression : Expression
{
    /// <summary>Creates a unary expression node.</summary>
    /// <param name="op">The operator token, either <see cref="TokenType.Minus"/> or <see cref="TokenType.Bang"/>.</param>
    /// <param name="operand">The operand expression.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    /// <exception cref="ArgumentException"><paramref name="op"/> is not a supported prefix operator.</exception>
    public UnaryExpression(TokenType op, Expression operand, int line, int column) : base(line, column)
    {
        if (op != TokenType.Minus && op != TokenType.Bang)
        {
            throw new ArgumentException($"'{op}' ist kein einstelliger Operator.", nameof(op));
        }

        Operator = op;
        Operand = operand;
    }

    /// <summary>The operator applied to <see cref="Operand"/>.</summary>
    public TokenType Operator { get; }

    /// <summary>The operand expression.</summary>
    public Expression Operand { get; }
}

/// <summary>A binary operator application.</summary>
public sealed class BinaryExpression : Expression
{
    /// <summary>Creates a binary expression node.</summary>
    /// <param name="op">The operator token.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public BinaryExpression(TokenType op, Expression left, Expression right, int line, int column) : base(line, column)
    {
        Operator = op;
        Left = left;
        Right = right;
    }

    /// <summary>The operator applied to both operands.</summary>
    public TokenType Operator { get; }

    /// <summary>The left operand.</summary>
    public Expression Left { get; }

    /// <summary>The right operand.</summary>
    public Expression Right { get; }
}

/// <summary>
/// An assignment to a plain variable or to an array element.
/// </summary>
public sealed class AssignmentExpression : Expression
{
    /// <summary>Creates an assignment node.</summary>
    /// <param name="target">Either a <see cref="VariableReference"/> or an <see cref="IndexExpression"/>.</param>
    /// <param name="op">The assignment operator, including the compound variants.</param>
    /// <param name="value">The expression producing the new value.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    /// <exception cref="ArgumentException"><paramref name="target"/> cannot be assigned to.</exception>
    public AssignmentExpression(Expression target, TokenType op, Expression value, int line, int column) : base(line, column)
    {
        if (target is not (VariableReference or IndexExpression))
        {
            throw new ArgumentException("Nur Variablen und Array-Elemente sind zuweisbar.", nameof(target));
        }

        Target = target;
        Operator = op;
        Value = value;
    }

    /// <summary>The assignment target.</summary>
    public Expression Target { get; }

    /// <summary>The assignment operator.</summary>
    public TokenType Operator { get; }

    /// <summary>The expression producing the new value.</summary>
    public Expression Value { get; }
}

/// <summary>An array element read such as <c>scores[i]</c>.</summary>
public sealed class IndexExpression : Expression
{
    /// <summary>Creates an index read node.</summary>
    /// <param name="target">The expression producing the array.</param>
    /// <param name="index">The expression producing the element index.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public IndexExpression(Expression target, Expression index, int line, int column) : base(line, column)
    {
        Target = target;
        Index = index;
    }

    /// <summary>The expression producing the array.</summary>
    public Expression Target { get; }

    /// <summary>The expression producing the element index.</summary>
    public Expression Index { get; }
}

/// <summary>A call of a script function or of a registered native.</summary>
public sealed class CallExpression : Expression
{
    /// <summary>Creates a call node.</summary>
    /// <param name="callee">Function name, possibly dotted such as <c>Player.GiveMoney</c>.</param>
    /// <param name="arguments">Argument expressions in source order.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public CallExpression(string callee, IReadOnlyList<Expression> arguments, int line, int column) : base(line, column)
    {
        Callee = callee;
        Arguments = arguments;
    }

    /// <summary>Function name, possibly dotted.</summary>
    public string Callee { get; }

    /// <summary>Argument expressions in source order.</summary>
    public IReadOnlyList<Expression> Arguments { get; }
}

/// <summary>An expression evaluated only for its result value.</summary>
public sealed class ExpressionStatement : Statement
{
    /// <summary>Creates an expression statement node.</summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public ExpressionStatement(Expression expression, int line, int column) : base(line, column)
        => Expression = expression;

    /// <summary>The evaluated expression.</summary>
    public Expression Expression { get; }
}

/// <summary>A variable declaration introduced by <c>new</c> or <c>var</c>.</summary>
public sealed class VariableDeclaration : Statement
{
    /// <summary>Creates a variable declaration node.</summary>
    /// <param name="name">Declared variable name.</param>
    /// <param name="initializer">Optional initial value, <c>null</c> when the declaration has none.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public VariableDeclaration(string name, Expression? initializer, int line, int column) : base(line, column)
    {
        Name = name;
        Initializer = initializer;
    }

    /// <summary>Declared variable name.</summary>
    public string Name { get; }

    /// <summary>Initial value expression, or <c>null</c>.</summary>
    public Expression? Initializer { get; }
}

/// <summary>An <c>if</c> statement with an optional <c>else</c> branch.</summary>
public sealed class IfStatement : Statement
{
    /// <summary>Creates an if statement node.</summary>
    /// <param name="condition">The condition expression.</param>
    /// <param name="thenBranch">Branch executed when the condition is true.</param>
    /// <param name="elseBranch">Optional branch executed when the condition is false.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public IfStatement(Expression condition, Statement thenBranch, Statement? elseBranch, int line, int column)
        : base(line, column)
    {
        Condition = condition;
        ThenBranch = thenBranch;
        ElseBranch = elseBranch;
    }

    /// <summary>The condition expression.</summary>
    public Expression Condition { get; }

    /// <summary>Branch for a true condition.</summary>
    public Statement ThenBranch { get; }

    /// <summary>Branch for a false condition, or <c>null</c>.</summary>
    public Statement? ElseBranch { get; }
}

/// <summary>A <c>while</c> loop.</summary>
public sealed class WhileStatement : Statement
{
    /// <summary>Creates a while statement node.</summary>
    /// <param name="condition">Re-evaluated before every iteration.</param>
    /// <param name="body">The loop body.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public WhileStatement(Expression condition, Statement body, int line, int column) : base(line, column)
    {
        Condition = condition;
        Body = body;
    }

    /// <summary>The loop condition.</summary>
    public Expression Condition { get; }

    /// <summary>The loop body.</summary>
    public Statement Body { get; }
}

/// <summary>A three clause <c>for</c> loop.</summary>
public sealed class ForStatement : Statement
{
    /// <summary>Creates a for statement node.</summary>
    /// <param name="initializer">Statement executed once before the loop, may be <c>null</c>.</param>
    /// <param name="condition">Re-evaluated before every iteration, may be <c>null</c>.</param>
    /// <param name="update">Statement executed after every iteration, may be <c>null</c>.</param>
    /// <param name="body">The loop body.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public ForStatement(Statement? initializer, Expression? condition, Statement? update, Statement body, int line, int column)
        : base(line, column)
    {
        Initializer = initializer;
        Condition = condition;
        Update = update;
        Body = body;
    }

    /// <summary>Initialiser statement, or <c>null</c>.</summary>
    public Statement? Initializer { get; }

    /// <summary>Loop condition, or <c>null</c> for an endless loop.</summary>
    public Expression? Condition { get; }

    /// <summary>Update statement, or <c>null</c>.</summary>
    public Statement? Update { get; }

    /// <summary>The loop body.</summary>
    public Statement Body { get; }
}

/// <summary>A <c>foreach (item in collection)</c> loop over an array.</summary>
public sealed class ForEachStatement : Statement
{
    /// <summary>Creates a foreach statement node.</summary>
    /// <param name="variableName">Name the loop variable is bound to in every iteration.</param>
    /// <param name="collection">Expression producing the array.</param>
    /// <param name="body">The loop body.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public ForEachStatement(string variableName, Expression collection, Statement body, int line, int column)
        : base(line, column)
    {
        VariableName = variableName;
        Collection = collection;
        Body = body;
    }

    /// <summary>Name of the loop variable.</summary>
    public string VariableName { get; }

    /// <summary>Expression producing the iterated array.</summary>
    public Expression Collection { get; }

    /// <summary>The loop body.</summary>
    public Statement Body { get; }
}

/// <summary>A <c>return</c> statement, with an optional value.</summary>
public sealed class ReturnStatement : Statement
{
    /// <summary>Creates a return statement node.</summary>
    /// <param name="value">Returned value expression, or <c>null</c> for a bare <c>return;</c>.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public ReturnStatement(Expression? value, int line, int column) : base(line, column) => Value = value;

    /// <summary>Returned value expression, or <c>null</c>.</summary>
    public Expression? Value { get; }
}

/// <summary>A braced sequence of statements creating one scope level.</summary>
public sealed class BlockStatement : Statement
{
    /// <summary>Creates a block statement node.</summary>
    /// <param name="statements">The statements in source order.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public BlockStatement(IReadOnlyList<Statement> statements, int line, int column) : base(line, column)
        => Statements = statements;

    /// <summary>The statements in source order.</summary>
    public IReadOnlyList<Statement> Statements { get; }
}

/// <summary>A <c>break</c> statement.</summary>
public sealed class BreakStatement : Statement
{
    /// <summary>Creates a break statement node.</summary>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public BreakStatement(int line, int column) : base(line, column) { }
}

/// <summary>A <c>continue</c> statement.</summary>
public sealed class ContinueStatement : Statement
{
    /// <summary>Creates a continue statement node.</summary>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public ContinueStatement(int line, int column) : base(line, column) { }
}

/// <summary>A top level function declaration.</summary>
public sealed class FunctionDeclaration : Node
{
    /// <summary>Creates a function declaration node.</summary>
    /// <param name="name">Function name.</param>
    /// <param name="parameters">Parameter names in declaration order.</param>
    /// <param name="body">The function body.</param>
    /// <param name="line">One based line number.</param>
    /// <param name="column">One based column number.</param>
    public FunctionDeclaration(string name, IReadOnlyList<string> parameters, BlockStatement body, int line, int column)
        : base(line, column)
    {
        Name = name;
        Parameters = parameters;
        Body = body;
    }

    /// <summary>Function name.</summary>
    public string Name { get; }

    /// <summary>Parameter names in declaration order.</summary>
    public IReadOnlyList<string> Parameters { get; }

    /// <summary>The function body.</summary>
    public BlockStatement Body { get; }
}
