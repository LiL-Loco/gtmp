using GTMP.Scripting.Hosting;
using GTMP.Scripting.Language;

namespace GTMP.Scripting.Runtime;

/// <summary>
/// Tree walking interpreter for the supported Pawn subset.
/// </summary>
/// <remarks>
/// <para>
/// Two properties are non negotiable because the engine runs inside a 50 ms server tick:
/// </para>
/// <list type="bullet">
/// <item>
/// Every executed statement and every evaluated expression charges one step from a per invocation
/// budget. When the budget is exhausted a <see cref="ScriptRuntimeException"/> is thrown, so a
/// runaway <c>while (true) {}</c> in a game mode costs a bounded amount of time instead of the tick.
/// </item>
/// <item>
/// The interpreter holds no static mutable state. All execution state lives in an
/// <see cref="ExecutionState"/> created per top level invocation, which also makes re-entrant
/// calls from a native (a native calling back into a script) work without surprises.
/// </item>
/// </list>
/// <para>
/// <c>break</c>, <c>continue</c> and <c>return</c> are implemented as thrown signals rather than
/// status enums, because they have to escape arbitrarily deep expression and block nesting
/// without every evaluation method having to forward a status value.
/// </para>
/// </remarks>
public sealed class Interpreter
{
    /// <summary>Default number of steps one top level invocation may execute.</summary>
    public const int DefaultStepBudget = 100_000;

    /// <summary>Default recursion limit for script function calls.</summary>
    public const int DefaultMaxCallDepth = 64;

    private readonly NativeFunctionRegistry _natives;
    private readonly Dictionary<string, ScriptFunction> _functions = new(StringComparer.Ordinal);
    private readonly Scope _globals = new(parent: null);

    /// <summary>
    /// Creates an interpreter bound to a native registry.
    /// </summary>
    /// <param name="natives">Registry consulted for unknown function names, must not be null.</param>
    /// <param name="stepBudget">Steps allowed per top level invocation.</param>
    /// <param name="maxCallDepth">Maximum nesting of script function calls.</param>
    /// <exception cref="ArgumentNullException"><paramref name="natives"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A budget or depth limit is not positive.</exception>
    public Interpreter(NativeFunctionRegistry natives, int stepBudget = DefaultStepBudget, int maxCallDepth = DefaultMaxCallDepth)
    {
        ArgumentNullException.ThrowIfNull(natives);
        if (stepBudget <= 0) throw new ArgumentOutOfRangeException(nameof(stepBudget), stepBudget, "Das Schritt-Budget muss positiv sein.");
        if (maxCallDepth <= 0) throw new ArgumentOutOfRangeException(nameof(maxCallDepth), maxCallDepth, "Die Aufruf-Tiefe muss positiv sein.");

        _natives = natives;
        StepBudget = stepBudget;
        MaxCallDepth = maxCallDepth;
    }

    /// <summary>Steps allowed per top level invocation.</summary>
    public int StepBudget { get; }

    /// <summary>Maximum nesting of script function calls.</summary>
    public int MaxCallDepth { get; }

    /// <summary>The native registry this interpreter calls into.</summary>
    public NativeFunctionRegistry Natives => _natives;

    /// <summary>Names of all currently registered script functions.</summary>
    public IEnumerable<string> FunctionNames => _functions.Keys;

    /// <summary>
    /// Registers a script function so it can be called by name.
    /// </summary>
    /// <param name="function">The function to register.</param>
    /// <param name="replaceExisting">Whether an existing registration of the same name is replaced.</param>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The name is taken and <paramref name="replaceExisting"/> is false.</exception>
    public void Register(ScriptFunction function, bool replaceExisting = false)
    {
        ArgumentNullException.ThrowIfNull(function);

        if (!replaceExisting && _functions.ContainsKey(function.Name))
        {
            throw new InvalidOperationException(
                $"Die Skript-Funktion '{function.Name}' ist bereits registriert ({_functions[function.Name].ScriptName}).");
        }

        _functions[function.Name] = function;
    }

    /// <summary>
    /// Looks a script function up by name.
    /// </summary>
    /// <param name="name">Function name, case sensitive.</param>
    /// <returns>The function, or <c>null</c> when it is unknown.</returns>
    public ScriptFunction? FindFunction(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        return _functions.TryGetValue(name, out ScriptFunction? function) ? function : null;
    }

    /// <summary>
    /// Drops every function that came from the given script, which is how a reload drops the
    /// previous version of a file.
    /// </summary>
    /// <param name="scriptName">Script file name.</param>
    /// <returns>The number of removed functions.</returns>
    public int RemoveFunctionsFrom(string scriptName)
    {
        if (string.IsNullOrEmpty(scriptName))
        {
            return 0;
        }

        List<string> stale = new();
        foreach (KeyValuePair<string, ScriptFunction> entry in _functions)
        {
            if (string.Equals(entry.Value.ScriptName, scriptName, StringComparison.Ordinal))
            {
                stale.Add(entry.Key);
            }
        }

        foreach (string name in stale)
        {
            _functions.Remove(name);
        }

        return stale.Count;
    }

    /// <summary>
    /// Evaluates a script's top level variable declarations into the global scope.
    /// </summary>
    /// <param name="globals">The declarations, normally produced by the parser.</param>
    /// <param name="scriptName">File name used in error messages.</param>
    /// <remarks>
    /// Global state is what makes a game mode possible at all: a mode has to keep
    /// counters and player lists between ticks, and a function local cannot do that.
    /// </param>
    /// <exception cref="ScriptRuntimeException">An initializer failed or the budget was exhausted.</exception>
    public void DefineGlobals(IReadOnlyList<VariableDeclaration> globals, string scriptName)
    {
        if (globals is null || globals.Count == 0)
        {
            return;
        }

        ExecutionState state = new(StepBudget, scriptName);

        foreach (VariableDeclaration declaration in globals)
        {
            ExecuteStatement(declaration, _globals, state);
        }
    }

    /// <summary>
    /// Calls a script function directly, bypassing name resolution.
    /// </summary>
    /// <param name="function">The function to call.</param>
    /// <param name="args">Arguments, missing ones become 0 and surplus ones are rejected.</param>
    /// <returns>The return value, or <c>null</c> when the function returned nothing.</returns>
    /// <exception cref="ScriptRuntimeException">Arity mismatch, budget exceeded or the function failed.</exception>
    public object? Call(ScriptFunction function, object?[]? args = null)
    {
        ArgumentNullException.ThrowIfNull(function);
        return Call(function, args ?? [], new ExecutionState(StepBudget, function.ScriptName));
    }

    /// <summary>
    /// Calls a script function by name; a native is used as fallback.
    /// </summary>
    /// <param name="name">Function name as written in the script.</param>
    /// <param name="args">Arguments for the call.</param>
    /// <returns>The return value, or <c>null</c> for a void function.</returns>
    /// <exception cref="ScriptRuntimeException">The name is neither a script function nor a registered native.</exception>
    public object? Call(string name, object?[]? args = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        ScriptFunction? function = FindFunction(name);
        if (function is not null)
        {
            return Call(function, args ?? []);
        }

        if (_natives.IsRegistered(name))
        {
            return _natives.Invoke(name, args ?? []);
        }

        throw new ScriptRuntimeException($"Unbekannte Funktion '{name}'.", string.Empty, 0, 0);
    }

    private object? Call(ScriptFunction function, object?[] args, ExecutionState state)
    {
        if (args.Length != function.ParameterCount)
        {
            throw new ScriptRuntimeException(
                $"'{function.Name}' erwartet {function.ParameterCount} Argument(e), bekam aber {args.Length}.",
                function.ScriptName,
                function.Declaration.Line,
                function.Declaration.Column);
        }

        state.Depth++;
        if (state.Depth > MaxCallDepth)
        {
            throw new ScriptRuntimeException(
                $"Maximale Aufruf-Tiefe von {MaxCallDepth} überschritten, vermutlich eine Endlosrekursion.",
                function.ScriptName,
                function.Declaration.Line,
                function.Declaration.Column);
        }

        string previousFile = state.File;
        Scope local = new(_globals);
        for (int i = 0; i < args.Length; i++)
        {
            local.Define(function.Declaration.Parameters[i], args[i]);
        }

        state.File = function.ScriptName;
        try
        {
            ExecuteStatement(function.Declaration.Body, local, state);
            return null;
        }
        catch (ReturnSignal signal)
        {
            return signal.Value;
        }
        finally
        {
            state.File = previousFile;
            state.Depth--;
        }
    }

    private void ExecuteStatement(Statement statement, Scope scope, ExecutionState state)
    {
        Charge(state, statement);

        switch (statement)
        {
            case ExpressionStatement expressionStatement:
                Evaluate(expressionStatement.Expression, scope, state);
                return;

            case VariableDeclaration declaration:
            {
                object? value = declaration.Initializer is null ? 0d : Evaluate(declaration.Initializer, scope, state);
                scope.Define(declaration.Name, value);
                return;
            }

            case BlockStatement block:
            {
                Scope inner = new(scope);
                foreach (Statement child in block.Statements)
                {
                    ExecuteStatement(child, inner, state);
                }

                return;
            }

            case IfStatement ifStatement:
                if (ScriptValue.IsTruthy(Evaluate(ifStatement.Condition, scope, state)))
                {
                    ExecuteStatement(ifStatement.ThenBranch, scope, state);
                }
                else if (ifStatement.ElseBranch is not null)
                {
                    ExecuteStatement(ifStatement.ElseBranch, scope, state);
                }

                return;

            case WhileStatement whileStatement:
                ExecuteWhile(whileStatement, scope, state);
                return;

            case ForStatement forStatement:
                ExecuteFor(forStatement, scope, state);
                return;

            case ForEachStatement forEachStatement:
                ExecuteForEach(forEachStatement, scope, state);
                return;

            case ReturnStatement returnStatement:
                throw new ReturnSignal(returnStatement.Value is null ? null : Evaluate(returnStatement.Value, scope, state));

            case BreakStatement:
                throw BreakSignal.Instance;

            case ContinueStatement:
                throw ContinueSignal.Instance;

            default:
                throw new ScriptRuntimeException(
                    $"Nicht unterstützter Statement-Typ '{statement.GetType().Name}'.",
                    state.File,
                    statement.Line,
                    statement.Column);
        }
    }

    private void ExecuteWhile(WhileStatement statement, Scope scope, ExecutionState state)
    {
        while (true)
        {
            if (!ScriptValue.IsTruthy(Evaluate(statement.Condition, scope, state)))
            {
                return;
            }

            try
            {
                ExecuteStatement(statement.Body, scope, state);
            }
            catch (BreakSignal)
            {
                return;
            }
            catch (ContinueSignal)
            {
                // The condition is re-evaluated, which is what continue means in a while loop.
            }
        }
    }

    private void ExecuteFor(ForStatement statement, Scope scope, ExecutionState state)
    {
        Scope loopScope = new(scope);

        if (statement.Initializer is not null)
        {
            ExecuteStatement(statement.Initializer, loopScope, state);
        }

        while (statement.Condition is null || ScriptValue.IsTruthy(Evaluate(statement.Condition, loopScope, state)))
        {
            try
            {
                ExecuteStatement(statement.Body, loopScope, state);
            }
            catch (BreakSignal)
            {
                return;
            }
            catch (ContinueSignal)
            {
                // continue jumps to the update, not to the condition.
            }

            if (statement.Update is not null)
            {
                ExecuteStatement(statement.Update, loopScope, state);
            }
        }
    }

    private void ExecuteForEach(ForEachStatement statement, Scope scope, ExecutionState state)
    {
        object?[] array = ScriptValue.ToArray(Evaluate(statement.Collection, scope, state), state.File);
        Scope loopScope = new(scope);
        loopScope.Define(statement.VariableName, null);

        // The length is captured once: mutating the array inside the body must not extend the loop.
        int length = array.Length;
        for (int index = 0; index < length; index++)
        {
            loopScope.Define(statement.VariableName, array[index]);
            try
            {
                ExecuteStatement(statement.Body, loopScope, state);
            }
            catch (BreakSignal)
            {
                return;
            }
            catch (ContinueSignal)
            {
                // Next element.
            }
        }
    }

    private object? Evaluate(Expression expression, Scope scope, ExecutionState state)
    {
        Charge(state, expression);

        switch (expression)
        {
            case NumberLiteral number:
                return number.Value;

            case StringLiteral text:
                return text.Value;

            case BoolLiteral boolean:
                return boolean.Value;

            case NullLiteral:
                return null;

            case VariableReference variable:
            {
                if (scope.TryGet(variable.Name, out object? value))
                {
                    return value;
                }

                throw new ScriptRuntimeException(
                    $"Unbekannte Variable '{variable.Name}'. Deklarieren Sie sie mit 'new'.",
                    state.File,
                    variable.Line,
                    variable.Column);
            }

            case ArrayLiteralExpression arrayLiteral:
            {
                object?[] values = new object?[arrayLiteral.Elements.Count];
                for (int index = 0; index < values.Length; index++)
                {
                    values[index] = Evaluate(arrayLiteral.Elements[index], scope, state);
                }

                return values;
            }

            case UnaryExpression unary:
            {
                object? operand = Evaluate(unary.Operand, scope, state);
                if (unary.Operator == TokenType.Bang)
                {
                    return !ScriptValue.IsTruthy(operand);
                }

                return -ScriptValue.ToNumber(operand, state.File);
            }

            case BinaryExpression binary:
                return EvaluateBinary(binary, scope, state);

            case AssignmentExpression assignment:
                return EvaluateAssignment(assignment, scope, state);

            case IndexExpression indexExpression:
                return ReadIndex(Evaluate(indexExpression.Target, scope, state), Evaluate(indexExpression.Index, scope, state), state, indexExpression);

            case CallExpression call:
            {
                object?[] arguments = new object?[call.Arguments.Count];
                for (int argumentIndex = 0; argumentIndex < arguments.Length; argumentIndex++)
                {
                    arguments[argumentIndex] = Evaluate(call.Arguments[argumentIndex], scope, state);
                }

                return InvokeCallee(call.Callee, arguments, state, call);
            }

            default:
                throw new ScriptRuntimeException(
                    $"Nicht unterstützter Ausdruck '{expression.GetType().Name}'.",
                    state.File,
                    expression.Line,
                    expression.Column);
        }
    }

    private object? EvaluateBinary(BinaryExpression binary, Scope scope, ExecutionState state)
    {
        // Short circuit before evaluating the right hand side.
        if (binary.Operator == TokenType.And)
        {
            if (!ScriptValue.IsTruthy(Evaluate(binary.Left, scope, state)))
            {
                return false;
            }

            return ScriptValue.IsTruthy(Evaluate(binary.Right, scope, state));
        }

        if (binary.Operator == TokenType.Or)
        {
            if (ScriptValue.IsTruthy(Evaluate(binary.Left, scope, state)))
            {
                return true;
            }

            return ScriptValue.IsTruthy(Evaluate(binary.Right, scope, state));
        }

        object? left = Evaluate(binary.Left, scope, state);
        object? right = Evaluate(binary.Right, scope, state);

        switch (binary.Operator)
        {
            case TokenType.Equal:
                return ScriptValue.AreEqual(left, right);

            case TokenType.NotEqual:
                return !ScriptValue.AreEqual(left, right);

            case TokenType.Less:
                return ScriptValue.Compare(left, right, state.File) < 0;

            case TokenType.LessOrEqual:
                return ScriptValue.Compare(left, right, state.File) <= 0;

            case TokenType.Greater:
                return ScriptValue.Compare(left, right, state.File) > 0;

            case TokenType.GreaterOrEqual:
                return ScriptValue.Compare(left, right, state.File) >= 0;

            case TokenType.Plus:
                // Pawn concatenates when either side is a string, so a game mode can mix log text
                // and numbers without a format function.
                if (left is string || right is string)
                {
                    return ScriptValue.ToDisplayString(left) + ScriptValue.ToDisplayString(right);
                }

                return ScriptValue.ToNumber(left, state.File) + ScriptValue.ToNumber(right, state.File);

            case TokenType.Minus:
                return ScriptValue.ToNumber(left, state.File) - ScriptValue.ToNumber(right, state.File);

            case TokenType.Star:
                return ScriptValue.ToNumber(left, state.File) * ScriptValue.ToNumber(right, state.File);

            case TokenType.Slash:
            {
                double divisor = ScriptValue.ToNumber(right, state.File);
                if (divisor == 0d)
                {
                    throw new ScriptRuntimeException("Division durch 0.", state.File, binary.Line, binary.Column);
                }

                return ScriptValue.ToNumber(left, state.File) / divisor;
            }

            case TokenType.Percent:
            {
                double divisor = ScriptValue.ToNumber(right, state.File);
                if (divisor == 0d)
                {
                    throw new ScriptRuntimeException("Restbildung durch 0.", state.File, binary.Line, binary.Column);
                }

                return ScriptValue.ToNumber(left, state.File) % divisor;
            }

            default:
                throw new ScriptRuntimeException(
                    $"Operator '{binary.Operator}' wird nicht unterstützt.",
                    state.File,
                    binary.Line,
                    binary.Column);
        }
    }

    private object? EvaluateAssignment(AssignmentExpression assignment, Scope scope, ExecutionState state)
    {
        object? value = Evaluate(assignment.Value, scope, state);

        if (assignment.Target is VariableReference variable)
        {
            if (assignment.Operator != TokenType.Assign)
            {
                // Reading an undeclared variable yields Pawn's implicit zero instead of failing,
                // so 'new x; x += 1' keeps working.
                object? current = scope.TryGet(variable.Name, out object? existing) ? existing : 0d;
                value = ApplyCompound(assignment, current, value, state);
            }

            scope.Assign(variable.Name, value);
            return value;
        }

        IndexExpression indexTarget = (IndexExpression)assignment.Target;
        object?[] array = ScriptValue.ToArray(Evaluate(indexTarget.Target, scope, state), state.File);
        int arrayIndex = ToIndex(Evaluate(indexTarget.Index, scope, state), array.Length, state, indexTarget);

        if (assignment.Operator != TokenType.Assign)
        {
            value = ApplyCompound(assignment, array[arrayIndex], value, state);
        }

        array[arrayIndex] = value;
        return value;
    }

    /// <summary>
    /// Applies a compound assignment. Only numeric operands are accepted here, which keeps
    /// <c>x += 1</c> cheap; string growth belongs in a native.
    /// </summary>
    private object? ApplyCompound(AssignmentExpression assignment, object? current, object? operand, ExecutionState state)
    {
        double left = ScriptValue.ToNumber(current, state.File);
        double right = ScriptValue.ToNumber(operand, state.File);

        switch (assignment.Operator)
        {
            case TokenType.PlusAssign:
                return left + right;

            case TokenType.MinusAssign:
                return left - right;

            case TokenType.StarAssign:
                return left * right;

            case TokenType.SlashAssign:
                if (right == 0d)
                {
                    throw new ScriptRuntimeException("Division durch 0.", state.File, assignment.Line, assignment.Column);
                }

                return left / right;

            default:
                throw new ScriptRuntimeException(
                    $"Operator '{assignment.Operator}' wird nicht unterstützt.",
                    state.File,
                    assignment.Line,
                    assignment.Column);
        }
    }

    private object? ReadIndex(object? array, object? index, ExecutionState state, IndexExpression node)
    {
        object?[] target = ScriptValue.ToArray(array, state.File);
        return target[ToIndex(index, target.Length, state, node)];
    }

    private static int ToIndex(object? index, int length, ExecutionState state, IndexExpression node)
    {
        double raw = ScriptValue.ToNumber(index, state.File);

        if (double.IsNaN(raw) || raw != Math.Floor(raw) || raw < 0d || raw >= length)
        {
            throw new ScriptRuntimeException(
                $"Array-Index {ScriptValue.ToDisplayString(index)} liegt nicht in 0..{length - 1}.",
                state.File,
                node.Line,
                node.Column);
        }

        return (int)raw;
    }

    private object? InvokeCallee(string callee, object?[] arguments, ExecutionState state, CallExpression node)
    {
        ScriptFunction? function = FindFunction(callee);
        if (function is not null)
        {
            return Call(function, arguments, state);
        }

        if (_natives.IsRegistered(callee))
        {
            try
            {
                return _natives.Invoke(callee, arguments);
            }
            catch (ScriptRuntimeException ex)
            {
                // Re-throw with the call site position so the script author sees where it failed.
                throw new ScriptRuntimeException(ex.Message, state.File, node.Line, node.Column);
            }
            catch (Exception ex)
            {
                // A native that throws an unexpected CLR exception must not take down the tick.
                throw new ScriptRuntimeException(
                    $"Die native Funktion '{callee}' ist fehlgeschlagen: {ex.Message}",
                    state.File,
                    node.Line,
                    node.Column);
            }
        }

        throw new ScriptRuntimeException($"Unbekannte Funktion '{callee}'.", state.File, node.Line, node.Column);
    }

    private static void Charge(ExecutionState state, Node node)
    {
        state.Line = node.Line;
        state.Column = node.Column;

        if (--state.RemainingSteps < 0)
        {
            throw new ScriptRuntimeException(
                "Schritt-Budget der Skript-Ausführung überschritten, das Skript wurde abgebrochen.",
                state.File,
                node.Line,
                node.Column);
        }
    }

    /// <summary>
    /// Mutable state of one top level invocation: the step budget, the current call depth and the
    /// position last touched, which is what a runtime error reports.
    /// </summary>
    private sealed class ExecutionState
    {
        public ExecutionState(int remainingSteps, string file)
        {
            RemainingSteps = remainingSteps;
            File = file;
        }

        public int RemainingSteps { get; set; }

        public int Depth { get; set; }

        public string File { get; set; }

        public int Line { get; set; }

        public int Column { get; set; }
    }

    /// <summary>
    /// One variable scope. Reads walk outwards to the parent so a block can see the variables of
    /// its enclosing function; writes create the variable in the scope that owns it.
    /// </summary>
    private sealed class Scope
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
        private readonly Scope? _parent;

        public Scope(Scope? parent) => _parent = parent;

        public bool TryGet(string name, out object? value)
        {
            for (Scope? current = this; current is not null; current = current._parent)
            {
                if (current._values.TryGetValue(name, out value))
                {
                    return true;
                }
            }

            value = null;
            return false;
        }

        public void Define(string name, object? value) => _values[name] = value;

        public void Assign(string name, object? value)
        {
            for (Scope? current = this; current is not null; current = current._parent)
            {
                if (current._values.ContainsKey(name))
                {
                    current._values[name] = value;
                    return;
                }
            }

            _values[name] = value;
        }
    }

    /// <summary>Signal thrown by <c>return</c>.</summary>
    private sealed class ReturnSignal : Exception
    {
        public ReturnSignal(object? value) : base("return") => Value = value;

        public object? Value { get; }
    }

    /// <summary>Signal thrown by <c>break</c>; a singleton because it carries no payload.</summary>
    private sealed class BreakSignal : Exception
    {
        public static readonly BreakSignal Instance = new();

        private BreakSignal() : base("break") { }
    }

    /// <summary>Signal thrown by <c>continue</c>; a singleton because it carries no payload.</summary>
    private sealed class ContinueSignal : Exception
    {
        public static readonly ContinueSignal Instance = new();

        private ContinueSignal() : base("continue") { }
    }
}
