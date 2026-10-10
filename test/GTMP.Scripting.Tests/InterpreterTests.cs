using System.Diagnostics;
using GTMP.Scripting.Hosting;
using GTMP.Scripting.Language;
using GTMP.Scripting.Runtime;

namespace GTMP.Scripting.Tests;

/// <summary>Covers expressions, control flow and function calls of the interpreter.</summary>
public sealed class InterpreterTests
{
    [Fact]
    public void Interpreter_EvaluatesOperatorPrecedence()
    {
        object? result = ScriptFactory.Run("main() { return 1 + 2 * 3 - 6 / 2; }");

        Assert.Equal(4d, result);
    }

    [Fact]
    public void Interpreter_EvaluatesArithmeticAndRemainder()
    {
        object? result = ScriptFactory.Run("main() { return 7 % 3 + 10 * 2 - 5; }");

        Assert.Equal(16d, result);
    }

    [Fact]
    public void Interpreter_ShortCircuitsAnd()
    {
        bool touched = false;

        ScriptFactory.Run(
            "main() { new v = 0; if (false && Touch()) { v = 1; } return v; }",
            natives => natives.Register("Touch", _ =>
            {
                touched = true;
                return true;
            }));

        Assert.False(touched, "Die rechte Seite von '&&' darf bei einem falschen Operanden nicht ausgewertet werden.");
    }

    [Fact]
    public void Interpreter_ShortCircuitsOr()
    {
        object? result = ScriptFactory.Run(
            "Boom() { return 1 / 0; }\nmain() { new v = 0; if (true || Boom()) { v = 1; } return v; }");

        Assert.Equal(1d, result);
    }

    [Fact]
    public void Interpreter_AssignsToVariablesAndArrayIndices()
    {
        object? result = ScriptFactory.Run(
            "main()\n{\n    new a = 1;\n    a = 5;\n    new arr = [10, 20, 30];\n    new i = 1;\n    arr[i] = 99;\n    return a + arr[0] + arr[1] + arr[2];\n}");

        // a = 5, arr[1] = 99 -> 5 + 10 + 99 + 30.
        Assert.Equal(144d, result);
    }

    [Fact]
    public void Interpreter_SupportsCompoundAssignment()
    {
        object? result = ScriptFactory.Run(
            "main()\n{\n    new x = 10;\n    x += 5;\n    x -= 3;\n    x *= 2;\n    x /= 4;\n    new arr = [1];\n    arr[0] += 41;\n    return x + arr[0];\n}");

        // x = (10 + 5 - 3) * 2 / 4 = 6, arr[0] = 1 + 41 = 42.
        Assert.Equal(48d, result);
    }

    [Fact]
    public void Interpreter_RunsIfElseAndElseIfChain()
    {
        const string source = @"
Grade(n)
{
    if (n >= 90) { return 1; }
    else if (n >= 80) { return 2; }
    else if (n >= 70) { return 3; }
    else { return 4; }
}

main()
{
    new sum = 0;
    sum += Grade(95) + Grade(85) + Grade(75) + Grade(10);
    return sum;
}";

        Assert.Equal(10d, ScriptFactory.Run(source));
    }

    [Fact]
    public void Interpreter_RunsWhileLoop()
    {
        object? result = ScriptFactory.Run(
            "main()\n{\n    new i = 0;\n    new sum = 0;\n    while (i < 5) { sum += i; i += 1; }\n    return sum;\n}");

        Assert.Equal(10d, result);
    }

    [Fact]
    public void Interpreter_RunsForLoop()
    {
        object? result = ScriptFactory.Run(
            "main()\n{\n    new sum = 0;\n    for (new i = 0; i < 5; i += 1) { sum += i; }\n    return sum;\n}");

        Assert.Equal(10d, result);
    }

    [Fact]
    public void Interpreter_RunsForeachLoop()
    {
        object? result = ScriptFactory.Run(
            "main()\n{\n    new arr = [3, 5, 7];\n    new sum = 0;\n    foreach (item in arr) { sum += item; }\n    return sum;\n}");

        Assert.Equal(15d, result);
    }

    [Fact]
    public void Interpreter_HandlesBreakAndContinue()
    {
        object? result = ScriptFactory.Run(
            @"
main()
{
    new sum = 0;
    for (new i = 0; i < 10; i += 1)
    {
        if (i % 2 == 0) { continue; }
        if (i > 7) { break; }
        sum += i;
    }

    new j = 0;
    while (true)
    {
        j += 1;
        if (j == 3) { break; }
    }

    return sum + j;
}");

        // 1 + 3 + 5 + 7 = 16 plus j = 3.
        Assert.Equal(19d, result);
    }

    [Fact]
    public void Interpreter_ReturnsValuesFromFunctions()
    {
        object? result = ScriptFactory.Run("Add(a, b) { return a + b; }\nmain() { return Add(2, 3); }");

        Assert.Equal(5d, result);
    }

    [Fact]
    public void Interpreter_SupportsEarlyReturn()
    {
        object? result = ScriptFactory.Run(
            @"
FirstPositive(arr)
{
    foreach (v in arr)
    {
        if (v > 0) { return v; }
    }

    return 0;
}

main() { return FirstPositive([-1, -2, 7, 9]); }");

        Assert.Equal(7d, result);
    }

    [Fact]
    public void Interpreter_HandlesRecursion()
    {
        object? result = ScriptFactory.Run(
            @"
Factorial(n)
{
    if (n <= 1) { return 1; }
    return n * Factorial(n - 1);
}

main() { return Factorial(6); }");

        Assert.Equal(720d, result);
    }

    [Fact]
    public void Interpreter_ConcatenatesStrings()
    {
        object? result = ScriptFactory.Run(
            "main()\n{\n    new name = \"Spieler\";\n    new hp = 42;\n    new text = name + \" hat \" + hp + \" HP\";\n    return text;\n}");

        Assert.Equal("Spieler hat 42 HP", result);
    }

    [Fact]
    public void Interpreter_ComparesValuesAndBooleans()
    {
        object? result = ScriptFactory.Run(
            "main()\n{\n    new a = 3;\n    new b = 3;\n    new ok = 0;\n    if (a == b && a >= 3 && a <= 3 && !false && a != 4) { ok = 1; }\n    return ok;\n}");

        Assert.Equal(1d, result);
    }

    [Fact]
    public void Interpreter_CallsNativeFunctions()
    {
        object? result = ScriptFactory.Run(
            "main() { return Test.Double(21); }",
            natives => natives.Register("Test.Double", 1, args => ScriptValue.ToNumber(args[0], "n") * 2d));

        Assert.Equal(42d, result);
    }

    [Fact]
    public void Interpreter_ThrowsOnUnknownVariable()
    {
        ScriptRuntimeException error = Assert.Throws<ScriptRuntimeException>(
            () => ScriptFactory.Run("main() { return missing; }"));

        Assert.Contains("Unbekannte Variable 'missing'", error.Message, StringComparison.Ordinal);
        Assert.Equal("test.pwn", error.ScriptName);
    }

    [Fact]
    public void Interpreter_ThrowsNamingUnknownFunction()
    {
        ScriptRuntimeException error = Assert.Throws<ScriptRuntimeException>(
            () => ScriptFactory.Run("main() { return Player.GiveMoney(1); }"));

        Assert.Contains("Unbekannte Funktion 'Player.GiveMoney'", error.Message, StringComparison.Ordinal);
        Assert.Equal("test.pwn", error.ScriptName);
    }

    [Fact]
    public void Interpreter_ThrowsOnDivisionByZero()
    {
        ScriptRuntimeException error = Assert.Throws<ScriptRuntimeException>(
            () => ScriptFactory.Run("main() { new a = 1; return a / 0; }"));

        Assert.Contains("Division durch 0", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Interpreter_ThrowsOnArrayIndexOutOfRange()
    {
        ScriptRuntimeException error = Assert.Throws<ScriptRuntimeException>(
            () => ScriptFactory.Run("main() { new arr = [1]; return arr[5]; }"));

        Assert.Contains("Array-Index", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Interpreter_RejectsWrongArgumentCount()
    {
        ScriptRuntimeException error = Assert.Throws<ScriptRuntimeException>(
            () => ScriptFactory.Run("Add(a, b) { return a + b; }\nmain() { return Add(1); }"));

        Assert.Contains("erwartet 2 Argument(e)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Interpreter_WrapsUnexpectedNativeFailure()
    {
        ScriptRuntimeException error = Assert.Throws<ScriptRuntimeException>(
            () => ScriptFactory.Run(
                "main() { return Boom(); }",
                natives => natives.Register("Boom", _ =>
                {
                    throw new InvalidOperationException("kaputt");
                })));

        Assert.Contains("native Funktion 'Boom' ist fehlgeschlagen", error.Message, StringComparison.Ordinal);    }

    [Fact]
    public void Interpreter_StopsInfiniteLoopAtTheStepBudget()
    {
        Interpreter interpreter = new(new NativeFunctionRegistry(), stepBudget: 5_000);
        foreach (FunctionDeclaration declaration in Parser.ParseSource("Spin() { while (true) { } }", "loop.pwn"))
        {
            interpreter.Register(new ScriptFunction(declaration, "loop.pwn"));
        }

        long start = Stopwatch.GetTimestamp();
        ScriptRuntimeException error = Assert.Throws<ScriptRuntimeException>(() => interpreter.Call("Spin"));
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        Assert.Contains("Schritt-Budget", error.Message, StringComparison.Ordinal);
        Assert.Equal("loop.pwn", error.ScriptName);

        // The whole point of the budget: the tick gets its time back quickly.
        Assert.True(elapsed < TimeSpan.FromSeconds(5), $"Endlosschleife lief zu lange: {elapsed}.");
    }

    [Fact]
    public void Interpreter_StopsRunawayRecursionAtTheCallDepthLimit()
    {
        NativeFunctionRegistry natives = new();
        Interpreter interpreter = new(natives, maxCallDepth: 16);
        foreach (FunctionDeclaration declaration in Parser.ParseSource("Recurse() { return Recurse(); }", "rec.pwn"))
        {
            interpreter.Register(new ScriptFunction(declaration, "rec.pwn"));
        }

        ScriptRuntimeException error = Assert.Throws<ScriptRuntimeException>(() => interpreter.Call("Recurse"));

        Assert.Contains("Aufruf-Tiefe", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Interpreter_KeepsBlockScopesApart()
    {
        object? result = ScriptFactory.Run(
            @"
Outer()
{
    new x = 1;
    if (true)
    {
        new x = 2;
        if (true) { x = 3; }
    }

    return x;
}

main() { return Outer(); }");

        Assert.Equal(1d, result);
    }
}
