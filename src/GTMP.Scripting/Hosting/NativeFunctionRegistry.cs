using GTMP.Scripting.Language;
using GTMP.Scripting.Runtime;

namespace GTMP.Scripting.Hosting;

/// <summary>
/// Maps script function names to C# delegates.
/// </summary>
/// <remarks>
/// Lives next to the engine rather than inside it because the game specific bindings are
/// registered from the host project. The registry owns no static state, so two engines in the
/// same process (for example a headless test run) stay independent.
/// </remarks>
public sealed class NativeFunctionRegistry
{
    private readonly Dictionary<string, NativeFunction> _functions = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a native with an exact argument count.
    /// </summary>
    /// <param name="name">Name used by scripts, case sensitive.</param>
    /// <param name="argCount">Number of arguments the call site has to pass.</param>
    /// <param name="implementation">The C# implementation.</param>
    /// <exception cref="ScriptSyntaxException">The name is already registered or invalid.</exception>
    public void Register(string name, int argCount, NativeFunctionDelegate implementation)
        => Register(name, argCount, argCount, implementation);

    /// <summary>
    /// Registers a native that accepts any number of arguments.
    /// </summary>
    /// <param name="name">Name used by scripts, case sensitive.</param>
    /// <param name="implementation">The C# implementation.</param>
    /// <exception cref="ScriptSyntaxException">The name is already registered or invalid.</exception>
    public void Register(string name, NativeFunctionDelegate implementation)
        => Register(name, 0, int.MaxValue, implementation);

    /// <summary>
    /// Registers a native with an argument count range.
    /// </summary>
    /// <param name="name">Name used by scripts, case sensitive.</param>
    /// <param name="minArgs">Lowest accepted argument count.</param>
    /// <param name="maxArgs">Highest accepted argument count, <see cref="int.MaxValue"/> for variadic.</param>
    /// <param name="implementation">The C# implementation.</param>
    /// <exception cref="ScriptSyntaxException">
    /// The name is empty, the argument range is inconsistent, or the name is already registered.
    /// A silent overwrite would hide a typo in a binding at the cost of a confusing runtime error.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="implementation"/> is null.</exception>
    public void Register(string name, int minArgs, int maxArgs, NativeFunctionDelegate implementation)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ScriptSyntaxException("Der Name einer nativen Funktion darf nicht leer sein.", 0, 0);
        }

        if (implementation is null)
        {
            throw new ArgumentNullException(nameof(implementation));
        }

        if (minArgs < 0 || maxArgs < minArgs)
        {
            throw new ScriptSyntaxException(
                $"Ungültiger Argumentbereich für die native Funktion '{name}': {minArgs}..{maxArgs}.",
                0,
                0);
        }

        if (_functions.ContainsKey(name))
        {
            throw new ScriptSyntaxException($"Die native Funktion '{name}' ist bereits registriert.", 0, 0);
        }

        _functions.Add(name, new NativeFunction(name, minArgs, maxArgs, implementation));
    }

    /// <summary>
    /// Looks a native up by name.
    /// </summary>
    /// <param name="name">Name used by scripts.</param>
    /// <returns>The native, or <c>null</c> when nothing is registered under that name.</returns>
    public NativeFunction? Find(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        return _functions.TryGetValue(name, out NativeFunction? native) ? native : null;
    }

    /// <summary>
    /// Checks whether a name is bound.
    /// </summary>
    /// <param name="name">Name used by scripts.</param>
    /// <returns><c>true</c> when a native is registered.</returns>
    public bool IsRegistered(string name) => Find(name) is not null;

    /// <summary>Number of registered natives.</summary>
    public int Count => _functions.Count;

    /// <summary>Names of all registered natives.</summary>
    public IEnumerable<string> Names => _functions.Keys;

    /// <summary>
    /// Calls a native by name.
    /// </summary>
    /// <param name="name">Name used by scripts.</param>
    /// <param name="args">Arguments for the call, never <c>null</c>.</param>
    /// <returns>The value returned by the delegate.</returns>
    /// <exception cref="ScriptRuntimeException">The name is unknown or the argument count does not match.</exception>
    public object? Invoke(string name, object?[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        NativeFunction native = Find(name)
            ?? throw new ScriptRuntimeException($"Unbekannte Funktion '{name}'.", string.Empty, 0, 0);

        if (!native.Accepts(args.Length))
        {
            throw new ScriptRuntimeException(
                $"Die Funktion '{name}' erwartet {DescribeRange(native)}, bekam aber {args.Length} Argument(e).",
                string.Empty,
                0,
                0);
        }

        return native.Implementation(args);
    }

    private static string DescribeRange(NativeFunction native)
    {
        if (native.MinArgs == native.MaxArgs)
        {
            return $"{native.MinArgs} Argument(e)";
        }

        if (native.MaxArgs == int.MaxValue)
        {
            return $"mindestens {native.MinArgs} Argument(e)";
        }

        return $"{native.MinArgs} bis {native.MaxArgs} Argument(e)";
    }
}
