using GTMP.Scripting.Language;

namespace GTMP.Scripting.Tests;

/// <summary>Covers the shape of the parsed tree and the diagnostics of the parser.</summary>
public sealed class ParserTests
{
    [Fact]
    public void Parser_AppliesStandardPrecedence()
    {
        IReadOnlyList<FunctionDeclaration> program = Parser.ParseSource(
            "Test() { new r = 1 + 2 * 3; }",
            "prec.pwn");

        BlockStatement body = program[0].Body;

        // '+' must sit above '*', so the tree root is the '+' node.
        VariableDeclaration declaration = Assert.IsType<VariableDeclaration>(body.Statements[0]);
        BinaryExpression plus = Assert.IsType<BinaryExpression>(declaration.Initializer);
        Assert.Equal(TokenType.Plus, plus.Operator);
        Assert.Equal(1d, Assert.IsType<NumberLiteral>(plus.Left).Value);
        Assert.Equal(3d, Assert.IsType<NumberLiteral>(Assert.IsType<BinaryExpression>(plus.Right).Right).Value);
    }

    [Fact]
    public void Parser_ThrowsWithPositionOnMissingSemicolon()
    {
        ScriptSyntaxException error = Assert.Throws<ScriptSyntaxException>(
            () => Parser.ParseSource("Test()\n{\n    new x = 1\n    new y = 2;\n}", "bad.pwn"));

        Assert.Equal(4, error.Line);
        Assert.Contains("';'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_RejectsDuplicateFunctionParameters()
    {
        ScriptSyntaxException error = Assert.Throws<ScriptSyntaxException>(
            () => Parser.ParseSource("Test(a, a) { return a; }", "dup.pwn"));

        Assert.Contains("doppelt deklariert", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_RejectsDeclarationWithoutInitialiser()
    {
        ScriptSyntaxException error = Assert.Throws<ScriptSyntaxException>(
            () => Parser.ParseSource("Test() { new x; }", "bad.pwn"));

        Assert.Contains("Startwert", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_BuildsElseIfChain()
    {
        IReadOnlyList<FunctionDeclaration> program = Parser.ParseSource(
            "Test(n)\n{\n    if (n == 1) { return 1; }\n    else if (n == 2) { return 2; }\n    else { return 3; }\n}",
            "chain.pwn");

        BlockStatement body = program[0].Body;
        IfStatement root = Assert.IsType<IfStatement>(body.Statements[0]);

        Assert.NotNull(root.ElseBranch);
        Assert.IsType<IfStatement>(root.ElseBranch);
        Assert.IsType<BlockStatement>(Assert.IsType<IfStatement>(root.ElseBranch!).ElseBranch);
    }
}
